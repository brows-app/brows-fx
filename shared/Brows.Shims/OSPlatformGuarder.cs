using System;
using System.Runtime.Versioning;

namespace Brows;

internal static class OSPlatformGuarder {
#if NETFRAMEWORK
#else
    [SupportedOSPlatformGuard("windows")]
#endif
    public static bool IsWindows() =>
#if NETFRAMEWORK
        true
#else
        OperatingSystem.IsWindows()
#endif
        ;

}
