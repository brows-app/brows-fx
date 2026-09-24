using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Brows.IO.Helpers;

[TestFixture]
internal sealed class FileSystemTaskTest {
    private string _directory;

    [SetUp]
    public void SetUp() {
        _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(_directory)) {
            foreach (string file in Directory.GetFiles(_directory, "*", SearchOption.AllDirectories)) {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            foreach (string directory in Directory.GetDirectories(_directory, "*", SearchOption.AllDirectories)) {
                File.SetAttributes(directory, FileAttributes.Normal);
            }
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public void Existing_CancelledToken_ThrowsOperationCanceledException() {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await FileSystemTask.Existing(_directory, cancellation.Token));
    }

    [Test]
    public async Task Existing_NullPath_ReturnsNull() {
        Assert.That(await FileSystemTask.Existing(null, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task ExistingFile_NullPath_ReturnsNull() {
        Assert.That(await FileSystemTask.ExistingFile(null, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task ExistingDirectory_NullPath_ReturnsNull() {
        Assert.That(await FileSystemTask.ExistingDirectory(null, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task Existing_File_ReturnsFileInfo() {
        var path = Path.Combine(_directory, "file.txt");
        File.WriteAllText(path, "item");

        var result = await FileSystemTask.Existing(path, CancellationToken.None);

        Assert.That(result, Is.TypeOf<FileInfo>());
        Assert.That(result.FullName, Is.EqualTo(path));
    }

    [Test]
    public async Task Existing_Directory_ReturnsDirectoryInfo() {
        var path = Path.Combine(_directory, "folder");
        Directory.CreateDirectory(path);

        var result = await FileSystemTask.Existing(path, CancellationToken.None);

        Assert.That(result, Is.TypeOf<DirectoryInfo>());
        Assert.That(result.FullName, Is.EqualTo(path));
    }

    [Test]
    public async Task Existing_MissingPath_ReturnsNull() {
        var path = Path.Combine(_directory, "missing", "file.txt");

        var result = await FileSystemTask.Existing(path, CancellationToken.None);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task Nonexistent_ExistingFiles_ReturnsUnusedPath() {
        var path = Path.Combine(_directory, "item.txt");
        File.WriteAllText(path, "item");
        File.WriteAllText(Path.Combine(_directory, "item (1).txt"), "item");

        var result = await FileSystemTask.Nonexistent(path, null, CancellationToken.None);

        Assert.That(result, Is.EqualTo(Path.Combine(_directory, "item (2).txt")));
    }

    [Test]
    public async Task Nonexistent_ExistingDirectories_ReturnsUnusedPath() {
        var path = Path.Combine(_directory, "folder");
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(_directory, "folder (1)"));

        var result = await FileSystemTask.Nonexistent(path, null, CancellationToken.None);

        Assert.That(result, Is.EqualTo(Path.Combine(_directory, "folder (2)")));
    }

    [Test]
    public async Task Nonexistent_NameEndingInYear_KeepsYear() {
        var path = Path.Combine(_directory, "Movie (2020).mp4");
        File.WriteAllText(path, "item");

        var result = await FileSystemTask.Nonexistent(path, null, CancellationToken.None);

        Assert.That(result, Is.EqualTo(Path.Combine(_directory, "Movie (2020) (1).mp4")));
    }

    [Test]
    public async Task Nonexistent_CandidateIsDirectory_KeepsFileExtensionSplit() {
        var path = Path.Combine(_directory, "a.b");
        File.WriteAllText(path, "item");
        Directory.CreateDirectory(Path.Combine(_directory, "a (1).b"));

        var result = await FileSystemTask.Nonexistent(path, null, CancellationToken.None);

        Assert.That(result, Is.EqualTo(Path.Combine(_directory, "a (2).b")));
    }

    [Test]
    public async Task Nonexistent_DirectoryWithDot_DoesNotSplitExtension() {
        var path = Path.Combine(_directory, "v1.2");
        Directory.CreateDirectory(path);

        var result = await FileSystemTask.Nonexistent(path, null, CancellationToken.None);

        Assert.That(result, Is.EqualTo(Path.Combine(_directory, "v1.2 (1)")));
    }

    [Test]
    public async Task Nonexistent_CustomCollision_ReceivesOriginalPathTypeAndAttempt() {
        var path = Path.Combine(_directory, "folder");
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(path + "-1");
        var collision = new RecordingCollisionPrevention();

        var result = await FileSystemTask.Nonexistent(path, collision, CancellationToken.None);

        Assert.That(result, Is.EqualTo(path + "-2"));
        Assert.That(collision.Calls, Is.EqualTo(new[] { (path, true, 1), (path, true, 2) }));
    }

    [Test]
    public async Task Nonexistent_MissingPath_ReturnsPath() {
        var path = Path.Combine(_directory, "missing.txt");

        var result = await FileSystemTask.Nonexistent(path, new NoOpCollisionPrevention(), CancellationToken.None);

        Assert.That(result, Is.EqualTo(path));
    }

    [Test]
    public async Task Delete_MissingFile_Completes() {
        var file = new FileInfo(Path.Combine(_directory, "missing.txt"));

        await FileSystemTask.Delete(file, null, CancellationToken.None);

        Assert.That(File.Exists(file.FullName), Is.False);
    }

    [Test]
    public async Task Delete_ManyFiles_DeletesEntriesAndCompletesProgress() {
        var root = Path.Combine(_directory, "root");
        for (var d = 0; d < 3; d++) {
            var directory = Path.Combine(root, $"folder{d}");
            Directory.CreateDirectory(directory);
            for (var f = 0; f < 1000; f++) {
                File.WriteAllText(Path.Combine(directory, $"file{f:D4}.txt"), "item");
            }
        }
        var progress = new CountingProgress();

        await FileSystemTask.Delete(new DirectoryInfo(root), progress, CancellationToken.None);

        Assert.That(Directory.Exists(root), Is.False);
        Assert.That(progress.Target, Is.EqualTo(3004));
        Assert.That(progress.Progress, Is.EqualTo(3004));
    }

    [Test]
    public async Task Delete_LockedFile_ThrowsAndDeletesOtherFiles() {
        if (OperatingSystem.IsWindows() == false) {
            Assert.Ignore("Open files only block deletion on Windows.");
        }
        var root = Path.Combine(_directory, "root");
        Directory.CreateDirectory(root);
        var locked = Path.Combine(root, "locked.txt");
        File.WriteAllText(locked, "item");
        for (var f = 0; f < 100; f++) {
            File.WriteAllText(Path.Combine(root, $"file{f:D3}.txt"), "item");
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var error = default(Exception);

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None)) {
            try {
                await FileSystemTask.Delete(new DirectoryInfo(root), null, cancellation.Token);
            }
            catch (Exception exception) {
                error = exception;
            }
        }

        Assert.That(error, Is.InstanceOf<IOException>());
        Assert.That(Directory.GetFiles(root), Is.EqualTo(new[] { locked }));
    }

    [Test]
    public void Nonexistent_CollisionThatDoesNotChangePath_Throws() {
        string path = Path.Combine(_directory, "existing.txt");
        File.WriteAllText(path, "item");
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(5));

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await FileSystemTask.Nonexistent(path, new NoOpCollisionPrevention(), cancellation.Token));
    }

    [Test]
    public async Task Delete_NestedReadOnlyTree_DeletesEntriesAndCompletesProgress() {
        string nested = Path.Combine(_directory, "one", "two");
        Directory.CreateDirectory(nested);
        string file = Path.Combine(nested, "readonly.txt");
        File.WriteAllText(file, "item");
        File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        File.SetAttributes(Path.Combine(_directory, "one"),
            File.GetAttributes(Path.Combine(_directory, "one")) | FileAttributes.ReadOnly);
        File.SetAttributes(nested, File.GetAttributes(nested) | FileAttributes.ReadOnly);
        FileSystemProgress progress = new CountingProgress();

        await FileSystemTask.Delete(new DirectoryInfo(_directory), progress, CancellationToken.None);

        Assert.That(Directory.Exists(_directory), Is.False);
        Assert.That(((CountingProgress)progress).Target, Is.EqualTo(4));
        Assert.That(((CountingProgress)progress).Progress, Is.EqualTo(4));
    }

    [Test]
    public async Task Delete_DirectoryLink_DoesNotDeleteTarget() {
        string target = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        string targetFile = Path.Combine(target, "keep.txt");
        File.WriteAllText(targetFile, "keep");
        string link = Path.Combine(_directory, "link");
        try {
            try {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception exception) when (exception is IOException ||
                                              exception is UnauthorizedAccessException ||
                                              exception is PlatformNotSupportedException) {
                Assert.Ignore($"Directory symbolic links are unavailable: {exception.Message}");
            }

            await FileSystemTask.Delete(new DirectoryInfo(_directory), null, CancellationToken.None);

            Assert.That(File.Exists(targetFile), Is.True);
        }
        finally {
            if (Directory.Exists(_directory)) {
                Directory.Delete(_directory, recursive: true);
            }
            if (Directory.Exists(target)) {
                Directory.Delete(target, recursive: true);
            }
        }
    }

    private sealed class NoOpCollisionPrevention : FileSystemCollisionPrevention {
        public override string Rename(string path, bool isDirectory, int attempt) => path;
    }

    private sealed class RecordingCollisionPrevention : FileSystemCollisionPrevention {
        public List<(string Path, bool IsDirectory, int Attempt)> Calls { get; } = new();

        public override string Rename(string path, bool isDirectory, int attempt) {
            Calls.Add((path, isDirectory, attempt));
            return $"{path}-{attempt}";
        }
    }

    private sealed class CountingProgress : FileSystemProgress {
        private long _target;
        private long _progress;

        public long Target => Interlocked.Read(ref _target);
        public long Progress => Interlocked.Read(ref _progress);

        public override void AddToTarget(long value) => Interlocked.Add(ref _target, value);

        public override void AddToProgress(long value) => Interlocked.Add(ref _progress, value);

        public override void SetCurrentInfo(FileSystemInfo value) {
        }
    }
}
