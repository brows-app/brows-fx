using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Brows.IO.Helpers;

[TestFixture]
internal sealed class DirectoryInfoExtensionTest {
    private string _directory;

    [SetUp]
    public void SetUp() {
        _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public void EnumerateFileSystemInfosBreadthFirst_NullOptions_SearchesSubdirectories() {
        var child = Path.Combine(_directory, "folder");
        Directory.CreateDirectory(child);
        var nested = Path.Combine(child, "item.txt");
        File.WriteAllText(nested, "item");

        var items = new DirectoryInfo(_directory)
            .EnumerateFileSystemInfosBreadthFirst("*", null);

        Assert.That(items.Select(item => item.FullName), Does.Contain(nested));
    }

    [Test]
    public void EnumerateFileSystemInfosBreadthFirst_RecurseSubdirectoriesFalse_DoesNotRecurse() {
        var child = Path.Combine(_directory, "folder");
        Directory.CreateDirectory(child);
        var nested = Path.Combine(child, "item.txt");
        File.WriteAllText(nested, "item");

        var items = new DirectoryInfo(_directory)
            .EnumerateFileSystemInfosBreadthFirst("*", new EnumerationOptions { RecurseSubdirectories = false })
            .ToList();

        Assert.That(items.Select(item => item.FullName), Does.Contain(child));
        Assert.That(items.Select(item => item.FullName), Does.Not.Contain(nested));
    }

    [Test]
    public void EnumerateFileSystemInfosBreadthFirst_SearchPattern_DoesNotPruneUnmatchedDirectories() {
        string child = Path.Combine(_directory, "folder");
        Directory.CreateDirectory(child);
        string expected = Path.Combine(child, "match.txt");
        File.WriteAllText(expected, "item");

        IEnumerable<FileSystemInfo> items = new DirectoryInfo(_directory)
            .EnumerateFileSystemInfosBreadthFirst("*.txt", new EnumerationOptions { RecurseSubdirectories = true });

        Assert.That(items.Any(item => item.FullName == expected), Is.True);
    }

    [Test]
    public void EnumerateFileSystemInfosBreadthFirst_MaxRecursionDepth_StopsAtRequestedDepth() {
        string nested = Path.Combine(_directory, "one", "two");
        Directory.CreateDirectory(nested);
        string expected = Path.Combine(_directory, "one", "top.txt");
        string tooDeep = Path.Combine(nested, "deep.txt");
        File.WriteAllText(expected, "item");
        File.WriteAllText(tooDeep, "item");

        IEnumerable<FileSystemInfo> items = new DirectoryInfo(_directory)
            .EnumerateFileSystemInfosBreadthFirst("*", new EnumerationOptions {
                MaxRecursionDepth = 1,
                RecurseSubdirectories = true
            });

        Assert.That(items.Any(item => item.FullName == expected), Is.True);
        Assert.That(items.Any(item => item.FullName == tooDeep), Is.False);
    }

    [Test]
    public void EnumerateFileSystemInfosBreadthFirst_MaxRecursionDepthZero_DoesNotRecurse() {
        string child = Path.Combine(_directory, "one");
        Directory.CreateDirectory(child);
        string nestedFile = Path.Combine(child, "nested.txt");
        File.WriteAllText(nestedFile, "item");

        List<FileSystemInfo> items = new DirectoryInfo(_directory)
            .EnumerateFileSystemInfosBreadthFirst("*", new EnumerationOptions {
                MaxRecursionDepth = 0,
                RecurseSubdirectories = true
            })
            .ToList();

        Assert.That(items.Any(item => item.FullName == child), Is.True);
        Assert.That(items.Any(item => item.FullName == nestedFile), Is.False);
    }

    [TestCase("*.*")]
    [TestCase("*.")]
    [TestCase("*.txt")]
    public void EnumerateFileSystemInfosBreadthFirst_Win32MatchType_MatchesFrameworkResults(string pattern) {
        Directory.CreateDirectory(Path.Combine(_directory, "folder"));
        File.WriteAllText(Path.Combine(_directory, "noext"), "item");
        File.WriteAllText(Path.Combine(_directory, "file.txt"), "item");
        EnumerationOptions options = new() { MatchType = MatchType.Win32, MaxRecursionDepth = 0 };
        List<string> expected = new DirectoryInfo(_directory)
            .EnumerateFileSystemInfos(pattern, options)
            .Select(item => item.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        List<string> actual = new DirectoryInfo(_directory)
            .EnumerateFileSystemInfosBreadthFirst(pattern, options)
            .Select(item => item.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void EnumerateFileSystemInfosBreadthFirst_ReturnSpecialDirectories_DoesNotRecurseIntoThem() {
        Directory.CreateDirectory(Path.Combine(_directory, "child"));
        File.WriteAllText(Path.Combine(_directory, "child", "item.txt"), "item");

        IEnumerable<FileSystemInfo> items = new DirectoryInfo(_directory)
            .EnumerateFileSystemInfosBreadthFirst(
                "*",
                new EnumerationOptions { RecurseSubdirectories = true, ReturnSpecialDirectories = true });

        Assert.That(items.Any(item => item.Name == "item.txt"), Is.True);
    }

    [Test]
    public void EnumerateFileSystemInfosBreadthFirst_ReparsePoint_DoesNotTraverseTarget() {
        string target = Path.Combine(_directory, "target");
        string link = Path.Combine(_directory, "link");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "item.txt"), "item");
        try {
            Directory.CreateSymbolicLink(link, _directory);
        }
        catch (Exception exception) when (exception is IOException ||
                                          exception is UnauthorizedAccessException ||
                                          exception is PlatformNotSupportedException) {
            Assert.Ignore($"Directory symbolic links are unavailable: {exception.Message}");
        }

        List<FileSystemInfo> items = new DirectoryInfo(_directory)
            .EnumerateFileSystemInfosBreadthFirst("*", new EnumerationOptions {
                AttributesToSkip = 0,
                RecurseSubdirectories = true
            })
            .ToList();

        Assert.That(items.Count(item => item.FullName.StartsWith(link + Path.DirectorySeparatorChar)), Is.EqualTo(0));
    }
}
