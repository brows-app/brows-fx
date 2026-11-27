namespace Brows;

/*
 * The base type of ComponentExport, so composition needs this assembly. The library is needed
 * only when LibraryLocation runs, and then this assembly is the one that requests it.
 */
public abstract class ComponentMiddleBase {
    public static string Location => typeof(ComponentMiddleBase).Assembly.Location;

    public static string LibraryLocation() {
        return ComponentLibrary.Location;
    }
}
