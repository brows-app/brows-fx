#if NETFRAMEWORK
namespace System.Runtime.Versioning;

[AttributeUsage(AttributeTargets.Assembly |
                AttributeTargets.Class |
                AttributeTargets.Constructor |
                AttributeTargets.Enum |
                AttributeTargets.Event |
                AttributeTargets.Field |
                AttributeTargets.Interface |
                AttributeTargets.Method |
                AttributeTargets.Module |
                AttributeTargets.Property |
                AttributeTargets.Struct,
                AllowMultiple = true, Inherited = false)]
internal sealed class SupportedOSPlatformAttribute : Attribute {
    public SupportedOSPlatformAttribute(string platformName) {
    }
}
#endif
