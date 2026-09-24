using System;
using System.Reflection;

namespace Brows;

[TestFixture]
internal sealed class OSPlatformGuarderTest {
    [Test]
    public void IsWindows_ReturnsOperatingSystemStatus() {
        var type = AssemblyUnderTest.Type("Brows.OSPlatformGuarder");
        var method = type.GetMethod(
            name: "IsWindows",
            bindingAttr: BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        var actual = method.Invoke(obj: null, parameters: null);
#if NETFRAMEWORK
        var expected = true;
#else
        var expected = OperatingSystem.IsWindows();
#endif
        Assert.That(actual, Is.EqualTo(expected));
    }
}
