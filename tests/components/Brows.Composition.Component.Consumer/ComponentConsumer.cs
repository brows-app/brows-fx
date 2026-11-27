using Brows.Composition;

namespace Brows;

/*
 * Needs the library only after startup, when Probe runs.
 */
public sealed class ComponentConsumer : IExport {
    public string Probe() {
        return ComponentLibrary.Location;
    }
}
