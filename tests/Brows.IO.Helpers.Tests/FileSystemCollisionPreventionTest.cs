using System.IO;

namespace Brows.IO.Helpers;

[TestFixture]
internal sealed class FileSystemCollisionPreventionTest {
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

    [TestCase("file.txt", 1, "file (1).txt")]
    [TestCase("file.txt", 3, "file (3).txt")]
    [TestCase("noext", 2, "noext (2)")]
    [TestCase("archive.tar.gz", 1, "archive.tar (1).gz")]
    public void Rename_File_InsertsAttemptBeforeExtension(string fileName, int attempt, string expectedName) {
        var path = Path.Combine(_directory, fileName);

        var result = FileSystemCollisionPrevention.Default.Rename(path, isDirectory: false, attempt);

        Assert.That(result, Is.EqualTo(Path.Combine(_directory, expectedName)));
    }

    [TestCase("Movie (2020).mp4", "Movie (2020) (1).mp4")]
    [TestCase("file (1).txt", "file (1) (1).txt")]
    [TestCase("(3).txt", "(3) (1).txt")]
    [TestCase("name (٣).txt", "name (٣) (1).txt")]
    public void Rename_NameEndingInNumberInParentheses_KeepsNumber(string fileName, string expectedName) {
        var path = Path.Combine(_directory, fileName);

        var result = FileSystemCollisionPrevention.Default.Rename(path, isDirectory: false, 1);

        Assert.That(Path.GetFileName(result), Is.EqualTo(expectedName));
    }

    [Test]
    public void Rename_DirectoryWithDot_DoesNotSplitExtensionWithoutCheckingDisk() {
        var path = Path.Combine(_directory, "My.Folder");

        var result = FileSystemCollisionPrevention.Default.Rename(path, isDirectory: true, 1);

        Assert.That(result, Is.EqualTo(Path.Combine(_directory, "My.Folder (1)")));
    }

    [Test]
    public void Rename_FileNamedLikeExistingDirectory_SplitsExtension() {
        var path = Path.Combine(_directory, "My.Folder");
        Directory.CreateDirectory(path);

        var result = FileSystemCollisionPrevention.Default.Rename(path, isDirectory: false, 1);

        Assert.That(result, Is.EqualTo(Path.Combine(_directory, "My (1).Folder")));
    }

    [Test]
    public void Rename_DirectoryWithTrailingSeparator_ReturnsSiblingDirectory() {
        var path = Path.Combine(_directory, "My.Folder");

        var result = FileSystemCollisionPrevention.Default.Rename(path + Path.DirectorySeparatorChar, true, 1);

        Assert.That(result, Is.EqualTo(Path.Combine(_directory, "My.Folder (1)")));
    }

    [Test]
    public void Rename_DotFile_AppendsSuffixAfterWholeName() {
        var path = Path.Combine(_directory, ".gitignore");

        var result = FileSystemCollisionPrevention.Default.Rename(path, isDirectory: false, 1);

        Assert.That(Path.GetFileName(result), Is.EqualTo(".gitignore (1)"));
    }

    [Test]
    public void Rename_NullPath_ThrowsArgumentNullException() {
        Assert.Throws<ArgumentNullException>(() => FileSystemCollisionPrevention.Default.Rename(null, false, 1));
    }

    [Test]
    public void Rename_Root_ThrowsArgumentException() {
        var root = Path.GetPathRoot(_directory);

        Assert.Throws<ArgumentException>(() => FileSystemCollisionPrevention.Default.Rename(root, true, 1));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Rename_AttemptLessThanOne_ThrowsArgumentOutOfRangeException(int attempt) {
        var path = Path.Combine(_directory, "file.txt");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FileSystemCollisionPrevention.Default.Rename(path, false, attempt));
    }
}