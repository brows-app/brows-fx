#if NETFRAMEWORK
using Brows;
using System;
using System.Reflection;

namespace System.Runtime.Versioning;

[TestFixture]
internal sealed class SupportedOSPlatformAttributeTest {
    [Test]
    public void Constructor_CreatesAttribute() {
        var type = AssemblyUnderTest.Type("System.Runtime.Versioning.SupportedOSPlatformAttribute");
        var constructor = type.GetConstructor(
            bindingAttr: BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: [typeof(string)],
            modifiers: null);
        Assert.That(constructor, Is.Not.Null);
        var subject = constructor.Invoke(["windows"]);
        Assert.That(subject, Is.InstanceOf<Attribute>());
    }

    [Test]
    public void AttributeUsage_AllowsSupportedTargets() {
        var type = AssemblyUnderTest.Type("System.Runtime.Versioning.SupportedOSPlatformAttribute");
        var usage = type.GetCustomAttribute<AttributeUsageAttribute>();
        var expectedTargets =
            AttributeTargets.Assembly |
            AttributeTargets.Class |
            AttributeTargets.Constructor |
            AttributeTargets.Enum |
            AttributeTargets.Event |
            AttributeTargets.Field |
            AttributeTargets.Interface |
            AttributeTargets.Method |
            AttributeTargets.Module |
            AttributeTargets.Property |
            AttributeTargets.Struct;
        using (Assert.EnterMultipleScope()) {
            Assert.That(usage.ValidOn, Is.EqualTo(expectedTargets));
            Assert.That(usage.AllowMultiple, Is.True);
            Assert.That(usage.Inherited, Is.False);
        }
    }
}
#endif
