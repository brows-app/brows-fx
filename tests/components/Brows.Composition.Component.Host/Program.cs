using Brows.Composition;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Brows;

/*
 * Usage: Brows.Composition.Component.Host <root> [--probe] [--kill]
 *
 * Composes <root>/a and <root>/b, and prints one line per observation:
 *   COMPOSED <types>, INIT-FAILED <exception>, PROBE <type> <result>, PROBE-FAILED <type> <exception>,
 *   KILLED, LOAD <location>, LOAD-FAILED <exception>.
 */
internal static class Program {
    private static readonly string[] ProbeOrder = ["ComponentExport", "ComponentConsumer"];

    private static string Describe(Exception exception) {
        var text = exception?.ToString() ?? "";
        if (exception is ReflectionTypeLoadException typeLoad) {
            text += string.Concat(typeLoad.LoaderExceptions.Select(loader => " | " + loader?.Message));
        }
        return text.Replace(Environment.NewLine, " ");
    }

    private static void Probe(IReadOnlyList<IExport> exports) {
        foreach (var name in ProbeOrder) {
            var export = exports.FirstOrDefault(item => item.GetType().Name == name);
            if (export is null) {
                continue;
            }
            try {
                var result = export.GetType().GetMethod("Probe").Invoke(export, null);
                Console.WriteLine($"PROBE {name} {result}");
            }
            catch (TargetInvocationException ex) {
                Console.WriteLine($"PROBE-FAILED {name} {Describe(ex.InnerException)}");
            }
        }
    }

    private static async Task<int> Main(string[] args) {
        var root = Path.GetFullPath(args[0]);
        var probe = args.Contains("--probe");
        var kill = args.Contains("--kill");
        var import = default(IImport);
        try {
            import = await Imports.Init(new ComponentEnvironment(root), CancellationToken.None);
            var exports = Imports.List<IExport>();
            var types = exports.Select(export => export.GetType().Name).OrderBy(type => type, StringComparer.Ordinal);
            Console.WriteLine($"COMPOSED {string.Join(",", types)}");
            if (probe) {
                Probe(exports);
            }
        }
        catch (Exception ex) {
            Console.WriteLine($"INIT-FAILED {Describe(ex)}");
        }
        if (kill && import is not null) {
            import.Kill();
            Console.WriteLine("KILLED");
        }
        try {
            var library = Assembly.Load("Brows.Composition.Component.Library");
            Console.WriteLine($"LOAD {library.Location}");
        }
        catch (Exception ex) {
            Console.WriteLine($"LOAD-FAILED {Describe(ex)}");
        }
        return 0;
    }

    private sealed class ComponentEnvironment : IImportEnvironment {
        public ImportInfo ImportInfo { get; }

        public ComponentEnvironment(string root) {
            ImportInfo = ImportInfo.Composed(path: () => new Dictionary<string, IEnumerable<string>> {
                [Path.Combine(root, "a")] = ["Brows.Composition.Component.*.dll"],
                [Path.Combine(root, "b")] = ["Brows.Composition.Component.*.dll"],
            });
        }
    }
}
