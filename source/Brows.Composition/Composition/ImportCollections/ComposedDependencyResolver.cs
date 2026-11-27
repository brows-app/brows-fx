using Domore.Logs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
#if NETCOREAPP
using System.Runtime.Loader;
#endif
using System.Threading;

namespace Brows.Composition.ImportCollections;

/*
 * Resolves the private dependencies of composed assemblies. An assembly composed from a file D/X.dll can keep its
 * dependencies in the directory D/X, and an assembly in any of those directories is loaded on demand when ordinary
 * probing does not find it.
 */
internal sealed class ComposedDependencyResolver : IDisposable {
    private static readonly ILog Log = Logging.For(typeof(ComposedDependencyResolver));
    private static readonly Version NoVersion = new(0, 0, 0, 0);

    private readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        Locker = new();

    private readonly IReadOnlyDictionary<string, Entry> Index;

    private bool Attached;
    private bool Disposed;

    private static Assembly Load(string path) {
        /*
         * Neither method enrolls the file's directory in the runtime's same-directory fallback
         * (as Assembly.LoadFrom does), so every dependency of a loaded file comes back here, where
         * the identity and version rules apply.
         */
#if NETCOREAPP
        return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
#else
        return Assembly.LoadFile(path);
#endif
    }

    private static Version VersionOf(AssemblyName name) {
        return name.Version ?? NoVersion;
    }

    private static bool TokensEqual(byte[] a, byte[] b) {
        /*
         * Call Enumerable explicitly. Extension syntax binds to the span overload
         * in MemoryExtensions, which needs System.Memory on .NET Framework.
         */
        return Enumerable.SequenceEqual(a ?? [], b ?? []);
    }

    private static bool TokenMatches(AssemblyName requested, AssemblyName candidate) {
        var requestedToken = requested.GetPublicKeyToken();
        if (requestedToken is null) {
            return true;
        }
        return TokensEqual(requestedToken, candidate.GetPublicKeyToken());
    }

    private static IEnumerable<string> DependencyFiles(string composedFile) {
        var directoryName = Path.GetDirectoryName(composedFile);
        if (string.IsNullOrEmpty(directoryName)) {
            return [];
        }
        var directory = Path.Combine(directoryName, Path.GetFileNameWithoutExtension(composedFile));
        if (Directory.Exists(directory) == false) {
            return [];
        }
        try {
            return Directory
                .GetFiles(directory)
                .Where(file => string.Equals(Path.GetExtension(file), ".dll", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) {
            if (Log.Warn()) {
                Log.Warn($"Dependency directory is not readable: {directory}", ex);
            }
            return [];
        }
    }

    private Assembly AssemblyResolve(object sender, ResolveEventArgs args) {
        var text = args?.Name;
        if (string.IsNullOrEmpty(text)) {
            return null;
        }
        var name = default(AssemblyName);
        try {
            name = new AssemblyName(text);
        }
        catch (Exception ex) {
            if (Log.Warn()) {
                Log.Warn($"Assembly name is not valid: {text}", ex);
            }
            return null;
        }
        try {
            return Resolve(name);
        }
        catch (Exception ex) {
            /*
             * The handler must never throw.
             */
            Log.Error(ex);
            return null;
        }
    }

    internal bool HasDependencies => Index.Count > 0;

    internal ComposedDependencyResolver(IEnumerable<string> composedFiles) :
        this(composedFiles, AssemblyName.GetAssemblyName, Load) {
    }

    internal ComposedDependencyResolver(IEnumerable<string> composedFiles,
                                        Func<string, AssemblyName> nameOf,
                                        Func<string, Assembly> load) {
        if (composedFiles is null) {
            throw new ArgumentNullException(nameof(composedFiles));
        }
        if (nameOf is null) {
            throw new ArgumentNullException(nameof(nameOf));
        }
        if (load is null) {
            throw new ArgumentNullException(nameof(load));
        }
        Index = composedFiles
            .Where(file => string.IsNullOrEmpty(file) == false)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(DependencyFiles)
            .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new Entry(
                    key: group.Key,
                    paths: [.. group.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)],
                    nameOf: nameOf,
                    load: load),
                StringComparer.OrdinalIgnoreCase);
    }

    internal Assembly Resolve(AssemblyName name) {
        if (name is null) {
            return null;
        }
        if (string.IsNullOrEmpty(name.Name)) {
            return null;
        }
        if (string.IsNullOrEmpty(name.CultureName) == false) {
            return null;
        }
        if (Index.TryGetValue(name.Name, out var entry) == false) {
            return null;
        }
        return entry.Resolve(name);
    }

    internal void Attach() {
        lock (Locker) {
            var attachable = HasDependencies && Attached == false && Disposed == false;
            if (attachable == false) {
                return;
            }
            AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolve;
            Attached = true;
        }
        if (Log.Info()) {
            Log.Info($"Resolving dependencies: {Index.Count}");
        }
    }

    void IDisposable.Dispose() {
        lock (Locker) {
            if (Disposed) {
                return;
            }
            Disposed = true;
            if (Attached) {
                AppDomain.CurrentDomain.AssemblyResolve -= AssemblyResolve;
                Attached = false;
            }
        }
    }

    private sealed class Entry {
        private readonly
#if NET9_0_OR_GREATER
            Lock
#else
            object
#endif
            Locker = new();

        private readonly string Key;
        private readonly IReadOnlyList<string> Paths;
        private readonly Func<string, AssemblyName> NameOf;
        private readonly Func<string, Assembly> Load;

        private IReadOnlyList<Candidate> Candidates;
        private Candidate Loaded;

        private IReadOnlyList<Candidate> ReadCandidates() {
            var candidates = new List<Candidate>();
            foreach (var path in Paths) {
                var name = default(AssemblyName);
                try {
                    name = NameOf(path);
                }
                catch (BadImageFormatException) {
                    /*
                     * Native libraries share the directory with managed assemblies.
                     */
                    continue;
                }
                catch (Exception ex) {
                    if (Log.Warn()) {
                        Log.Warn($"Dependency is not readable: {path}", ex);
                    }
                    continue;
                }
                var nameMatches =
                    name is not null &&
                    string.Equals(name.Name, Key, StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrEmpty(name.CultureName);
                if (nameMatches) {
                    candidates.Add(new Candidate(path, name));
                }
            }
            return candidates;
        }

        private Assembly TryLoad(Candidate candidate) {
            var assembly = default(Assembly);
            try {
                assembly = Load(candidate.FilePath);
            }
            catch (Exception ex) {
                if (Log.Error()) {
                    Log.Error($"Dependency failed to load: {candidate.FilePath}", ex);
                }
                return null;
            }
            var loadedName = assembly?.GetName();
            var identityMatches =
                loadedName is not null &&
                string.Equals(loadedName.Name, candidate.Name.Name, StringComparison.OrdinalIgnoreCase) &&
                VersionOf(loadedName) == VersionOf(candidate.Name) &&
                TokensEqual(loadedName.GetPublicKeyToken(), candidate.Name.GetPublicKeyToken());
            if (identityMatches == false) {
                if (Log.Error()) {
                    Log.Error($"Dependency loaded as {loadedName}, not {candidate.Name}: {candidate.FilePath}");
                }
                return null;
            }
            return assembly;
        }

        public Entry(string key,
                     IReadOnlyList<string> paths,
                     Func<string, AssemblyName> nameOf,
                     Func<string, Assembly> load) {
            Key = key;
            Paths = paths;
            NameOf = nameOf;
            Load = load;
        }

        public Assembly Resolve(AssemblyName requested) {
            var minimum = requested.Version;
            lock (Locker) {
                if (Loaded is not null) {
                    /*
                     * The process holds one assembly per simple name,
                     * so the loaded one is the only possible answer.
                     */
                    var loadedSatisfies =
                        TokenMatches(requested, Loaded.Name) &&
                        (minimum is null || VersionOf(Loaded.Name) >= minimum);
                    if (loadedSatisfies) {
                        return Loaded.Assembly;
                    }
                    if (Log.Warn()) {
                        Log.Warn($"Dependency {Loaded.Name} does not satisfy {requested}: {Loaded.FilePath}");
                    }
                    return null;
                }
                var candidates = Candidates ??= ReadCandidates();
                for (; ; ) {
                    /*
                     * OrderByDescending is stable, so equal versions keep the path order.
                     */
                    var candidate = candidates
                        .Where(c => c.Failed == false && TokenMatches(requested, c.Name))
                        .OrderByDescending(c => VersionOf(c.Name))
                        .FirstOrDefault();
                    if (candidate is null) {
                        return null;
                    }
                    var candidateTooLow = minimum is not null && VersionOf(candidate.Name) < minimum;
                    if (candidateTooLow) {
                        if (Log.Warn()) {
                            Log.Warn($"Dependency {candidate.Name} does not satisfy {requested}: {candidate.FilePath}");
                        }
                        return null;
                    }
                    var assembly = TryLoad(candidate);
                    if (assembly is null) {
                        candidate.Failed = true;
                        continue;
                    }
                    candidate.Assembly = assembly;
                    Loaded = candidate;
                    if (Log.Info()) {
                        Log.Info($"Dependency resolved: {candidate.Name}", candidate.FilePath);
                    }
                    return assembly;
                }
            }
        }
    }

    private sealed class Candidate {
        public string FilePath { get; }
        public AssemblyName Name { get; }
        public bool Failed { get; set; }
        public Assembly Assembly { get; set; }

        public Candidate(string filePath, AssemblyName name) {
            FilePath = filePath;
            Name = name;
        }
    }
}
