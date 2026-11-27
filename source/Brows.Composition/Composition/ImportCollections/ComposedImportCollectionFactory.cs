using Domore.Logs;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Brows.Composition.ImportCollections;

internal sealed class ComposedImportCollectionFactory : ImportCollectionFactory {
    private static readonly ILog Log = Logging.For(typeof(ComposedImportCollectionFactory));

    private sealed class AttributedPart {
        [ImportMany]
        public IEnumerable<IExport> Exports {
            get => field ??= [];
            set => field = value;
        }
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DefaultPath(CancellationToken token) {
        var path = new Dictionary<string, IReadOnlyList<string>> {
            { ".", ["Brows.*.dll",] },
            { "exports", ["Brows.*.dll"] },
        };
        var task = Task.Run(cancellationToken: token, action: () => {
            using (var process = Process.GetCurrentProcess()) {
                var exe = process?.MainModule?.FileName;
                var exeExists = exe is not null && File.Exists(exe);
                if (exeExists) {
                    var exeDir = Path.GetDirectoryName(exe);
                    var exeName = Path.GetFileName(exe);
                    path[exeDir] = [exeName];
                }
            }
        });
        await task;
        return path;
    }

    public ComposedImportInfo Info { get; }

    public ComposedImportCollectionFactory(ComposedImportInfo info) {
        Info = info ?? throw new ArgumentNullException(nameof(info));
    }

    public sealed override async Task<ImportCollection> Create(CancellationToken token) {
        var path = Info.Path;
        if (path is null || path.Count == 0) {
            path = await DefaultPath(token);
        }
        var imports = path;
        var exports = new List<IExport>(Info.Inject.Where(item => item is not null));
        List<IExport> List(List<IDisposable> resources) {
            /*
             * Create every catalog before composing any of them, so that the dependencies
             * of all composed files can be resolved while types are inspected.
             */
            var catalogs = new List<DirectoryCatalog>();
            foreach (var import in imports) {
                var path = import.Key;
                var patterns = import.Value ?? [];
                foreach (var pattern in patterns) {
                    if (Log.Info()) {
                        Log.Info($"Composing parts...",
                                 $"Path:    {path}",
                                 $"Pattern: {pattern}");
                    }
                    if (Directory.Exists(path) == false) {
                        if (Log.Warn()) {
                            Log.Warn($"Path does not exist: {path}");
                        }
                        continue;
                    }
                    var catalog = new DirectoryCatalog(
                        path, searchPattern: pattern);
                    resources.Add(catalog);
                    catalogs.Add(catalog);
                }
            }
            var resolver = new ComposedDependencyResolver(
                catalogs.SelectMany(catalog => catalog.LoadedFiles));
            resources.Add(resolver);
            resolver.Attach();
            foreach (var catalog in catalogs) {
                var part = new AttributedPart();
                var container = new CompositionContainer(catalog);
                resources.Add(container);
                try {
                    container.ComposeParts(part);
                }
                catch (ReflectionTypeLoadException ex) {
                    var loaderExceptions = ex?.LoaderExceptions;
                    if (loaderExceptions != null) {
                        foreach (var loaderException in loaderExceptions) {
                            if (Log.Warn()) {
                                Log.Warn("Loader Exception", loaderException);
                            }
                        }
                    }
                    throw;
                }
                if (Log.Info()) {
                    Log.Info($"Imports: {part.Exports.Count()}",
                             $"----------------",
                             part.Exports.Select(export => export?.GetType()));
                }
                exports.AddRange(part.Exports);
            }
            return exports;
        }
        return await Task.Run(cancellationToken: token, function: () => {
            var resources = new List<IDisposable>();
            try {
                var exports = List(resources);
                var exportCount = exports.Count;
                if (Log.Info()) {
                    Log.Info($"Total exports imported: {exportCount}");
                }
                return new ImportCollection(exports, resources);
            }
            catch {
                foreach (var resource in Enumerable.Reverse(resources)) {
                    try {
                        resource?.Dispose();
                    }
                    catch (Exception ex) {
                        Log.Error(ex);
                    }
                }
                throw;
            }
        });
    }
}
