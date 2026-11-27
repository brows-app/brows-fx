using Brows.Composition;
using System;
using System.Threading.Tasks;

namespace Brows;

public sealed class ComponentExport : ComponentMiddleBase, IExportAndInit {
    public string Probe() {
        return $"{LibraryLocation()}|{Location}";
    }

    Task IExportAndInit.Init(IExportContext context, CancellationToken token) {
        if (Environment.GetEnvironmentVariable("BROWS_COMPONENT_FAIL_INIT") == "1") {
            throw new InvalidOperationException("The component's initialization failed.");
        }
        return Task.CompletedTask;
    }
}
