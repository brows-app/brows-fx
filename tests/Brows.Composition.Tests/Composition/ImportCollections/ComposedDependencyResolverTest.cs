using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Brows.Composition.ImportCollections;

[TestFixture]
internal sealed class ComposedDependencyResolverTest {
    private const string SignedToken = "0123456789abcdef";
    private const string OtherToken = "fedcba9876543210";

    private string Root;
    private Dictionary<string, Func<AssemblyName>> Names;
    private Dictionary<string, Func<Assembly>> Loaders;
    private List<string> Loads;

    private string Composed(string relativePath) {
        var path = Path.GetFullPath(Path.Combine(Root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, []);
        return path;
    }

    private string Dependency(string relativePath, Func<AssemblyName> name) {
        var path = Composed(relativePath);
        Names[path] = name;
        return path;
    }

    private string Dependency(string relativePath, string name) {
        return Dependency(relativePath, () => new AssemblyName(name));
    }

    private AssemblyName NameOf(string path) {
        if (Names.TryGetValue(path, out var name)) {
            return name();
        }
        throw new FileNotFoundException(path);
    }

    private Assembly Load(string path) {
        Loads.Add(path);
        if (Loaders.TryGetValue(path, out var loader)) {
            return loader();
        }
        return new FakeAssembly(NameOf(path));
    }

    private ComposedDependencyResolver Resolver(params string[] composedFiles) {
        return new ComposedDependencyResolver(composedFiles, NameOf, Load);
    }

    private static string NameIn(Assembly assembly) {
        return assembly?.GetName()?.FullName;
    }

    [SetUp]
    public void SetUp() {
        Root = Path.Combine(Path.GetTempPath(), nameof(ComposedDependencyResolverTest), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Names = new(StringComparer.OrdinalIgnoreCase);
        Loaders = new(StringComparer.OrdinalIgnoreCase);
        Loads = [];
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(Root)) {
            Directory.Delete(Root, recursive: true);
        }
    }

    [Test]
    public void Resolve_DependencyBesideComposedFile_LoadsIt() {
        var composed = Composed("P.dll");
        var dependency = Dependency(@"P\Dep.dll", "Dep, Version=1.0.0.0");
        var resolver = Resolver(composed);
        var assembly = resolver.Resolve(new AssemblyName("Dep, Version=1.0.0.0"));
        using (Assert.EnterMultipleScope()) {
            Assert.That(NameIn(assembly), Is.EqualTo(new AssemblyName("Dep, Version=1.0.0.0").FullName));
            Assert.That(Loads, Is.EqualTo(new[] { dependency }));
            Assert.That(resolver.HasDependencies, Is.True);
        }
    }

    [Test]
    public void Resolve_SameNameTwice_ReturnsSameAssembly() {
        var composed = Composed("P.dll");
        Dependency(@"P\Dep.dll", "Dep, Version=1.0.0.0");
        var resolver = Resolver(composed);
        var first = resolver.Resolve(new AssemblyName("Dep"));
        var second = resolver.Resolve(new AssemblyName("Dep, Version=1.0.0.0"));
        using (Assert.EnterMultipleScope()) {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.SameAs(first));
            Assert.That(Loads, Has.Count.EqualTo(1));
        }
    }

    [TestCase("Dep")]
    [TestCase("Dep, Version=1.0.0.0")]
    [TestCase("Dep, Version=2.0.0.0")]
    public void Resolve_SeveralDirectories_LoadsHighestVersion(string requested) {
        var a = Composed("A.dll");
        var b = Composed("B.dll");
        Dependency(@"A\Dep.dll", "Dep, Version=1.0.0.0");
        var higher = Dependency(@"B\Dep.dll", "Dep, Version=2.0.0.0");
        var resolver = Resolver(a, b);
        var assembly = resolver.Resolve(new AssemblyName(requested));
        using (Assert.EnterMultipleScope()) {
            Assert.That(assembly?.GetName().Version, Is.EqualTo(new Version(2, 0, 0, 0)));
            Assert.That(Loads, Is.EqualTo(new[] { higher }));
        }
    }

    [Test]
    public void Resolve_RequestedVersionAboveAvailable_ReturnsNull() {
        var composed = Composed("P.dll");
        Dependency(@"P\Dep.dll", "Dep, Version=1.0.0.0");
        var resolver = Resolver(composed);
        var assembly = resolver.Resolve(new AssemblyName("Dep, Version=2.0.0.0"));
        using (Assert.EnterMultipleScope()) {
            Assert.That(assembly, Is.Null);
            Assert.That(Loads, Is.Empty);
        }
    }

    [Test]
    public void Resolve_AfterLoad_LaterHigherRequestReturnsNull() {
        var a = Composed("A.dll");
        var b = Composed("B.dll");
        Dependency(@"A\Dep.dll", "Dep, Version=1.0.0.0");
        var higher = Dependency(@"B\Dep.dll", "Dep, Version=2.0.0.0");
        Loaders[higher] = () => throw new FileLoadException(); // So that 1.0 is the one loaded.
        var resolver = Resolver(a, b);
        var first = resolver.Resolve(new AssemblyName("Dep, Version=1.0.0.0"));
        var second = resolver.Resolve(new AssemblyName("Dep, Version=2.0.0.0"));
        using (Assert.EnterMultipleScope()) {
            Assert.That(first?.GetName().Version, Is.EqualTo(new Version(1, 0, 0, 0)));
            Assert.That(second, Is.Null);
            Assert.That(Loads, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Resolve_PublicKeyToken_FiltersBeforeRanking() {
        var a = Composed("A.dll");
        var b = Composed("B.dll");
        var signed = Dependency(@"A\Dep.dll", $"Dep, Version=1.0.0.0, PublicKeyToken={SignedToken}");
        Dependency(@"B\Dep.dll", $"Dep, Version=2.0.0.0, PublicKeyToken={OtherToken}");
        var resolver = Resolver(a, b);
        var assembly = resolver.Resolve(new AssemblyName($"Dep, Version=1.0.0.0, PublicKeyToken={SignedToken}"));
        using (Assert.EnterMultipleScope()) {
            Assert.That(assembly?.GetName().Version, Is.EqualTo(new Version(1, 0, 0, 0)));
            Assert.That(Loads, Is.EqualTo(new[] { signed }));
        }
    }

    [Test]
    public void Resolve_PublicKeyTokenWithoutCompatibleCandidate_ReturnsNull() {
        var composed = Composed("P.dll");
        Dependency(@"P\Dep.dll", $"Dep, Version=2.0.0.0, PublicKeyToken={OtherToken}");
        var resolver = Resolver(composed);
        var assembly = resolver.Resolve(new AssemblyName($"Dep, Version=1.0.0.0, PublicKeyToken={SignedToken}"));
        using (Assert.EnterMultipleScope()) {
            Assert.That(assembly, Is.Null);
            Assert.That(Loads, Is.Empty);
        }
    }

    [Test]
    public void Resolve_UnsignedRequest_RejectsSigned() {
        var composed = Composed("P.dll");
        Dependency(@"P\Dep.dll", $"Dep, Version=1.0.0.0, PublicKeyToken={SignedToken}");
        var resolver = Resolver(composed);
        var assembly = resolver.Resolve(new AssemblyName("Dep, Version=1.0.0.0, PublicKeyToken=null"));
        Assert.That(assembly, Is.Null);
    }

    [Test]
    public void Resolve_CultureSpecificRequest_ReturnsNull() {
        var composed = Composed("P.dll");
        Dependency(@"P\Dep.dll", "Dep, Version=1.0.0.0");
        var resolver = Resolver(composed);
        var assembly = resolver.Resolve(new AssemblyName("Dep, Version=1.0.0.0, Culture=fr"));
        using (Assert.EnterMultipleScope()) {
            Assert.That(assembly, Is.Null);
            Assert.That(Loads, Is.Empty);
        }
    }

    [Test]
    public void Resolve_CultureSpecificCandidate_ReturnsNull() {
        var composed = Composed("P.dll");
        Dependency(@"P\Dep.dll", "Dep, Version=1.0.0.0, Culture=fr");
        var resolver = Resolver(composed);
        var assembly = resolver.Resolve(new AssemblyName("Dep, Version=1.0.0.0"));
        Assert.That(assembly, Is.Null);
    }

    [TestCase("Other")]
    [TestCase("P")]
    public void Resolve_UnknownOrComposedName_ReturnsNull(string requested) {
        var composed = Composed("P.dll");
        Dependency(@"P\Dep.dll", "Dep, Version=1.0.0.0");
        var resolver = Resolver(composed);
        var assembly = resolver.Resolve(new AssemblyName(requested));
        using (Assert.EnterMultipleScope()) {
            Assert.That(assembly, Is.Null);
            Assert.That(Loads, Is.Empty);
        }
    }

    [Test]
    public void Resolve_NativeFile_IsSkipped() {
        var a = Composed("A.dll");
        var b = Composed("B.dll");
        Dependency(@"A\Dep.dll", () => throw new BadImageFormatException());
        var managed = Dependency(@"B\Dep.dll", "Dep, Version=1.0.0.0");
        var resolver = Resolver(a, b);
        var assembly = resolver.Resolve(new AssemblyName("Dep"));
        using (Assert.EnterMultipleScope()) {
            Assert.That(assembly, Is.Not.Null);
            Assert.That(Loads, Is.EqualTo(new[] { managed }));
        }
    }

    [Test]
    public void Resolve_NameMismatch_ReturnsNull() {
        var composed = Composed("P.dll");
        Dependency(@"P\Dep.dll", "Other, Version=1.0.0.0");
        var resolver = Resolver(composed);
        using (Assert.EnterMultipleScope()) {
            Assert.That(resolver.Resolve(new AssemblyName("Dep")), Is.Null);
            Assert.That(resolver.Resolve(new AssemblyName("Other")), Is.Null);
            Assert.That(Loads, Is.Empty);
        }
    }

    [Test]
    public void Resolve_LoadedIdentityMismatch_ReturnsNull() {
        var composed = Composed("P.dll");
        var dependency = Dependency(@"P\Dep.dll", "Dep, Version=1.0.0.0");
        Loaders[dependency] = () => new FakeAssembly(new AssemblyName("Dep, Version=3.0.0.0"));
        var resolver = Resolver(composed);
        var first = resolver.Resolve(new AssemblyName("Dep"));
        var second = resolver.Resolve(new AssemblyName("Dep"));
        using (Assert.EnterMultipleScope()) {
            Assert.That(first, Is.Null);
            Assert.That(second, Is.Null);
            Assert.That(Loads, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void Resolve_LoadThrows_TriesNextCandidateOnce() {
        var a = Composed("A.dll");
        var b = Composed("B.dll");
        var lower = Dependency(@"A\Dep.dll", "Dep, Version=1.0.0.0");
        var higher = Dependency(@"B\Dep.dll", "Dep, Version=2.0.0.0");
        Loaders[higher] = () => throw new FileLoadException();
        var resolver = Resolver(a, b);
        var first = resolver.Resolve(new AssemblyName("Dep"));
        var second = resolver.Resolve(new AssemblyName("Dep"));
        using (Assert.EnterMultipleScope()) {
            Assert.That(first?.GetName().Version, Is.EqualTo(new Version(1, 0, 0, 0)));
            Assert.That(second, Is.SameAs(first));
            Assert.That(Loads, Is.EqualTo(new[] { higher, lower }));
        }
    }

    [Test]
    public void Constructor_NestedOrMissingDirectories_AreNotIndexed() {
        var p = Composed("P.dll");
        var q = Composed("Q.dll");
        Dependency(@"P\sub\Dep.dll", "Dep, Version=1.0.0.0");
        Dependency(@"P\Dep.pdb", "Dep, Version=1.0.0.0");
        var resolver = Resolver(p, q);
        using (Assert.EnterMultipleScope()) {
            Assert.That(resolver.HasDependencies, Is.False);
            Assert.That(resolver.Resolve(new AssemblyName("Dep")), Is.Null);
        }
    }

    [Test]
    public void Constructor_SameComposedFileTwice_IndexesItOnce() {
        var composed = Composed("P.dll");
        Dependency(@"P\Dep.dll", "Dep, Version=1.0.0.0");
        var resolver = Resolver(composed, composed.ToUpperInvariant());
        var assembly = resolver.Resolve(new AssemblyName("Dep"));
        using (Assert.EnterMultipleScope()) {
            Assert.That(assembly, Is.Not.Null);
            Assert.That(Loads, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void Dispose_AfterAttach_IsIdempotent() {
        var composed = Composed("P.dll");
        Dependency(@"P\Dep.dll", "Dep, Version=1.0.0.0");
        var resolver = Resolver(composed);
        resolver.Attach();
        resolver.Attach();
        IDisposable disposable = resolver;
        Assert.That(() => {
            disposable.Dispose();
            disposable.Dispose();
        }, Throws.Nothing);
    }

    [Test]
    public void Attach_AfterDispose_DoesNothing() {
        var composed = Composed("P.dll");
        Dependency(@"P\Dep.dll", "Dep, Version=1.0.0.0");
        var resolver = Resolver(composed);
        ((IDisposable)resolver).Dispose();
        Assert.That(resolver.Attach, Throws.Nothing);
    }

    private sealed class FakeAssembly : Assembly {
        private readonly AssemblyName Name;

        public FakeAssembly(AssemblyName name) {
            Name = name;
        }

        public override AssemblyName GetName(bool copiedName) {
            return new AssemblyName(Name.FullName);
        }
    }
}
