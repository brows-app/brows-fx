#if NET10_0
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Brows.Composition.ImportCollections;

/*
 * Stages compiled components and composes them in a new host process for each case, so that MEF, the real
 * AssemblyResolve event and each runtime's loader take part. Every case runs on .NET Framework and .NET.
 */
[TestFixture("net48")]
[TestFixture("net10.0")]
internal sealed class ComposedDependencyResolutionTest {
    private const string Export = "Brows.Composition.Component.Export";
    private const string Middle = "Brows.Composition.Component.Middle";
    private const string Library = "Brows.Composition.Component.Library";
    private const string LibraryV1 = "Brows.Composition.Component.Library.V1";
    private const string Consumer = "Brows.Composition.Component.Consumer";
    private const string Host = "Brows.Composition.Component.Host";

    private static readonly string ComponentBin = Metadata("BrowsComponentBin");
    private static readonly string ComponentConfiguration = Metadata("BrowsComponentConfiguration");

    private readonly string Framework;

    private string Root;

    private string ExportDirectory => Path.Combine(Root, "a", Export);
    private string ConsumerDirectory => Path.Combine(Root, "b", Consumer);

    private static string Metadata(string key) {
        return typeof(ComposedDependencyResolutionTest).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == key)
            .Value;
    }

    private static bool Under(string path, string directory) {
        return path?.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string Find(IReadOnlyList<string> lines, string prefix) {
        return lines
            .Where(line => line.StartsWith(prefix + " ", StringComparison.Ordinal))
            .Select(line => line.Substring(prefix.Length + 1))
            .FirstOrDefault();
    }

    private string Output(string project) {
        return Path.Combine(ComponentBin, project, $"{ComponentConfiguration}_{Framework}");
    }

    private void Stage(string project, string relativeDirectory) {
        var assembly = project == LibraryV1 ? Library : project;
        var source = Path.Combine(Output(project), assembly + ".dll");
        var directory = Path.Combine(Root, relativeDirectory);
        Directory.CreateDirectory(directory);
        File.Copy(source, Path.Combine(directory, assembly + ".dll"));
    }

    private void StageDiscovery() {
        Stage(Export, "a");
        Stage(Middle, Path.Combine("a", Export));
        Stage(Library, Path.Combine("a", Export));
        Stage(Consumer, "b");
    }

    private IReadOnlyList<string> Run(bool failInit = false, params string[] options) {
        var start = new ProcessStartInfo {
            FileName = Path.Combine(Output(Host), Host + ".exe"),
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(Root);
        foreach (var option in options) {
            start.ArgumentList.Add(option);
        }
        start.Environment["BROWS_COMPONENT_FAIL_INIT"] = failInit ? "1" : "0";
        using (var process = Process.Start(start)) {
            var output = process.StandardOutput.ReadToEndAsync();
            var exited = process.WaitForExit(milliseconds: 60000);
            if (exited == false) {
                process.Kill(entireProcessTree: true);
                Assert.Fail("The component host did not exit.");
            }
            var lines = output.Result.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, lines));
            return lines;
        }
    }

    public ComposedDependencyResolutionTest(string framework) {
        Framework = framework;
    }

    [SetUp]
    public void SetUp() {
        Root = Path.Combine(Path.GetTempPath(), nameof(ComposedDependencyResolutionTest), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(Root)) {
            Directory.Delete(Root, recursive: true);
        }
    }

    [Test]
    public void Compose_WithoutDependencyDirectory_Fails() {
        Stage(Export, "a");
        var lines = Run(options: "--probe");
        using (Assert.EnterMultipleScope()) {
            Assert.That(Find(lines, "INIT-FAILED"), Does.Contain(Middle));
            Assert.That(Find(lines, "COMPOSED"), Is.Null);
        }
    }

    [Test]
    public void Compose_DependencyDirectory_ResolvesDuringDiscoveryAndAfterStartup() {
        StageDiscovery();
        var lines = Run(options: "--probe");
        var export = Find(lines, "PROBE ComponentExport")?.Split('|');
        var consumer = Find(lines, "PROBE ComponentConsumer");
        Assert.That(export, Has.Length.EqualTo(2), string.Join(Environment.NewLine, lines));
        using (Assert.EnterMultipleScope()) {
            Assert.That(Find(lines, "COMPOSED"), Is.EqualTo("ComponentConsumer,ComponentExport"));
            Assert.That(Under(export[0], ExportDirectory), Is.True, export[0]);
            Assert.That(Under(export[1], ExportDirectory), Is.True, export[1]);
            Assert.That(Under(consumer, ExportDirectory), Is.True, consumer);
        }
    }

    [Test]
    public void Compose_CompetingVersions_LoadsHighestEvenWhenColocatedIsLower() {
        Stage(Export, "a");
        Stage(Middle, Path.Combine("a", Export));
        Stage(LibraryV1, Path.Combine("a", Export));
        Stage(Consumer, "b");
        Stage(Library, Path.Combine("b", Consumer));
        var lines = Run(options: "--probe");
        var export = Find(lines, "PROBE ComponentExport")?.Split('|');
        var consumer = Find(lines, "PROBE ComponentConsumer");
        Assert.That(export, Has.Length.EqualTo(2), string.Join(Environment.NewLine, lines));
        using (Assert.EnterMultipleScope()) {
            Assert.That(Under(export[0], ConsumerDirectory), Is.True, export[0]);
            Assert.That(Under(export[1], ExportDirectory), Is.True, export[1]);
            Assert.That(Under(consumer, ConsumerDirectory), Is.True, consumer);
        }
    }

    [Test]
    public void Compose_OnlyLowerVersion_FailsTheReference() {
        Stage(Export, "a");
        Stage(Middle, Path.Combine("a", Export));
        Stage(LibraryV1, Path.Combine("a", Export));
        var lines = Run(options: "--probe");
        using (Assert.EnterMultipleScope()) {
            Assert.That(Find(lines, "COMPOSED"), Is.EqualTo("ComponentExport"));
            Assert.That(Find(lines, "PROBE-FAILED ComponentExport"), Does.Contain($"{Library}, Version=2.0.0.0"));
            Assert.That(Find(lines, "PROBE ComponentExport"), Is.Null);
        }
    }

    [Test]
    public void Compose_InitFails_DetachesResolver() {
        StageDiscovery();
        var lines = Run(failInit: true);
        using (Assert.EnterMultipleScope()) {
            Assert.That(Find(lines, "INIT-FAILED"), Does.Contain("The component's initialization failed."));
            Assert.That(Find(lines, "LOAD-FAILED"), Does.Contain(Library));
            Assert.That(Find(lines, "LOAD"), Is.Null);
        }
    }

    [Test]
    public void Kill_DetachesResolver() {
        StageDiscovery();
        var lines = Run(options: "--kill");
        using (Assert.EnterMultipleScope()) {
            Assert.That(lines, Does.Contain("KILLED"));
            Assert.That(Find(lines, "LOAD-FAILED"), Does.Contain(Library));
            Assert.That(Find(lines, "LOAD"), Is.Null);
        }
    }

    [Test]
    public void Compose_KeptAlive_ResolvesOnDemand() {
        StageDiscovery();
        var lines = Run();
        var location = Find(lines, "LOAD");
        Assert.That(Under(location, ExportDirectory), Is.True, string.Join(Environment.NewLine, lines));
    }
}
#endif
