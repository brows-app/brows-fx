using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Brows.IO.Helpers;

[TestFixture]
internal sealed class FileSystemPathTest {
    [Test]
    public void SkipCommonOf_DifferentDriveRoots_PreservesDistinctRelativePaths() {
        IEnumerable<(string OriginalPath, string RelativePath)> result =
            FileSystemPath.SkipCommonOf([@"C:\alpha\same.txt", @"D:\alpha\same.txt"], StringComparer.OrdinalIgnoreCase);

        int uniquePaths = result.Select(item => item.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        Assert.That(uniquePaths, Is.EqualTo(2));
    }

    [Test]
    public void SkipCommonOf_DifferentDriveRoots_OmitsVolumeSeparator() {
        if (OperatingSystem.IsWindows() == false) {
            Assert.Ignore("Drive letters are only available on Windows.");
        }

        List<string> result = FileSystemPath
            .SkipCommonOf([@"C:\alpha\same.txt", @"D:\alpha\same.txt"], StringComparer.OrdinalIgnoreCase)
            .Select(item => item.RelativePath)
            .ToList();

        Assert.That(result, Is.EqualTo(new[] { @"C\alpha\same.txt", @"D\alpha\same.txt" }));
    }

    [Test]
    public void SkipCommonOf_BacktrackToDriveRoot_OmitsVolumeSeparator() {
        if (OperatingSystem.IsWindows() == false) {
            Assert.Ignore("Drive letters are only available on Windows.");
        }

        IEnumerable<(string OriginalPath, string RelativePath)> result =
            FileSystemPath.SkipCommonOf([@"C:\file.txt"], StringComparer.OrdinalIgnoreCase, backtrack: 1);

        Assert.That(result.Single().RelativePath, Is.EqualTo(@"C\file.txt"));
    }

    [Test]
    public void SkipCommonOf_SingleFileWithBacktrack_PreservesParentDirectory() {
        string path = Path.Combine(Path.GetTempPath(), "parent", "file.txt");

        IEnumerable<(string OriginalPath, string RelativePath)> result =
            FileSystemPath.SkipCommonOf([path], StringComparer.OrdinalIgnoreCase, backtrack: 1);

        Assert.That(result.Single().RelativePath, Is.EqualTo(Path.Combine("parent", "file.txt")));
    }

    [Test]
    public void CommonOf_AbsolutePaths_PreservesRoot() {
        string root = Path.GetPathRoot(Path.GetTempPath());
        string first = Path.Combine(root, "first");
        string second = Path.Combine(root, "second");

        string result = FileSystemPath.CommonOf([first, second], StringComparer.OrdinalIgnoreCase);

        Assert.That(result, Is.EqualTo(root));
    }
}
