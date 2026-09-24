#if NETFRAMEWORK
using Brows;

namespace System.Runtime.CompilerServices;

[TestFixture]
internal sealed class IsExternalInitTest {
    [Test]
    public void Type_IsDefined() {
        var type = AssemblyUnderTest.Type("System.Runtime.CompilerServices.IsExternalInit");
        using (Assert.EnterMultipleScope()) {
            Assert.That(type.IsClass, Is.True);
            Assert.That(type.IsAbstract, Is.True);
            Assert.That(type.IsSealed, Is.True);
        }
    }
}
#endif
