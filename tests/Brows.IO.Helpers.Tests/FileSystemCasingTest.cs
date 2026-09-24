using System;
using System.IO;

namespace Brows.IO.Helpers;

[TestFixture]
internal sealed class FileSystemCasingTest {
    private string _directory;

    [SetUp]
    public void SetUp() {
        _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_directory, "Existing"));
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public void CorrectCasing_MissingDescendant_PreservesEveryPathSegment() {
        string existing = OperatingSystem.IsWindows() ? "existing" : "Existing";
        string path = Path.Combine(_directory, existing, "missing", "child.txt");

        string result = new FileInfo(path).CorrectCasing();

        Assert.That(result, Is.EqualTo(Path.Combine(_directory, "Existing", "missing", "child.txt")));
    }

    [Test]
    public void CorrectCasing_LowercaseDriveLetter_ReturnsUppercaseDriveLetter() {
        if (OperatingSystem.IsWindows() == false || _directory.Length < 2 || _directory[1] != ':') {
            Assert.Ignore("Drive letters are only available for Windows drive paths.");
        }
        string path = char.ToLowerInvariant(_directory[0]) + _directory[1..];

        string result = new DirectoryInfo(path).CorrectCasing();

        Assert.That(result[0], Is.EqualTo(char.ToUpperInvariant(_directory[0])));
        Assert.That(result, Is.EqualTo(_directory).IgnoreCase);
    }
}
