using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Brows.IO.Helpers;

[TestFixture]
internal sealed class DirectoryInfoAsyncEnumerableTest {
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
    public void EnumerateFileSystemInfosAsync_MissingRoot_ThrowsDirectoryNotFoundException() {
        DirectoryInfo missing = new(Path.Combine(_directory, "missing"));
        DirectoryInfoAsyncEnumerable enumerable = missing.EnumerateFileSystemInfosAsync("*", new EnumerationOptions());

        Assert.ThrowsAsync<DirectoryNotFoundException>(async () => {
            await foreach (FileSystemInfo item in enumerable) {
            }
        });
    }

    [Test]
    public void EnumerateFileSystemInfosAsync_Failure_ThrowsOriginalException() {
        DirectoryInfo missing = new(Path.Combine(_directory, "missing"));
        DirectoryInfoAsyncEnumerable enumerable = missing.EnumerateFileSystemInfosAsync("*", new EnumerationOptions());
        enumerable.EnumerateReparsePoints = true;

        Assert.ThrowsAsync<DirectoryNotFoundException>(async () => {
            await foreach (FileSystemInfo item in enumerable) {
            }
        });
    }

    [Test]
    public void EnumerateFileSystemInfosAsync_NullDirectory_ThrowsImmediately() {
        DirectoryInfo directory = null;

        Assert.Throws<ArgumentNullException>(() => directory.EnumerateFileSystemInfosAsync("*", null));
    }

    [Test]
    public void EnumerateFileSystemInfosAsync_NullSearchPattern_ThrowsImmediately() {
        DirectoryInfo directory = new(_directory);

        Assert.Throws<ArgumentNullException>(() => directory.EnumerateFileSystemInfosAsync(null, null));
    }

    [Test]
    public async Task EnumerateFileSystemInfosAsync_SpecialDirectoriesAndRecursion_Completes() {
        Directory.CreateDirectory(Path.Combine(_directory, "child"));
        File.WriteAllText(Path.Combine(_directory, "child", "item.txt"), "item");
        EnumerationOptions options = new() {
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = true
        };
        DirectoryInfoAsyncEnumerable enumerable = new DirectoryInfo(_directory)
            .EnumerateFileSystemInfosAsync("*", options);
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(5));
        List<FileSystemInfo> items = new();

        await foreach (FileSystemInfo item in enumerable.WithCancellation(cancellation.Token)) {
            items.Add(item);
        }

        Assert.That(items.Exists(item => item.FullName.EndsWith("item.txt", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public async Task EnumerateFileSystemInfosAsync_SearchPattern_DoesNotPruneUnmatchedDirectories() {
        Directory.CreateDirectory(Path.Combine(_directory, "folder"));
        string expected = Path.Combine(_directory, "folder", "match.txt");
        File.WriteAllText(expected, "item");
        File.WriteAllText(Path.Combine(_directory, "folder", "ignore.bin"), "item");
        DirectoryInfoAsyncEnumerable enumerable = new DirectoryInfo(_directory).EnumerateFileSystemInfosAsync(
            "*.txt",
            new EnumerationOptions { RecurseSubdirectories = true });
        List<FileSystemInfo> items = new();

        await foreach (FileSystemInfo item in enumerable) {
            items.Add(item);
        }

        Assert.That(items.Exists(item => item.FullName == expected), Is.True);
        Assert.That(items.Exists(item => item.Name == "ignore.bin"), Is.False);
    }

    [Test]
    public async Task EnumerateFileSystemInfosAsync_MaxRecursionDepth_StopsAtRequestedDepth() {
        string nested = Path.Combine(_directory, "one", "two");
        Directory.CreateDirectory(nested);
        string expected = Path.Combine(_directory, "one", "top.txt");
        string tooDeep = Path.Combine(nested, "deep.txt");
        File.WriteAllText(expected, "item");
        File.WriteAllText(tooDeep, "item");
        DirectoryInfoAsyncEnumerable enumerable = new DirectoryInfo(_directory).EnumerateFileSystemInfosAsync(
            "*",
            new EnumerationOptions { MaxRecursionDepth = 1, RecurseSubdirectories = true });
        List<FileSystemInfo> items = new();

        await foreach (FileSystemInfo item in enumerable) {
            items.Add(item);
        }

        Assert.That(items.Exists(item => item.FullName == expected), Is.True);
        Assert.That(items.Exists(item => item.FullName == tooDeep), Is.False);
    }

    [Test]
    public async Task EnumerateFileSystemInfosAsync_MaxRecursionDepthZero_DoesNotRecurse() {
        string child = Path.Combine(_directory, "one");
        Directory.CreateDirectory(child);
        string nestedFile = Path.Combine(child, "nested.txt");
        File.WriteAllText(nestedFile, "item");
        DirectoryInfoAsyncEnumerable enumerable = new DirectoryInfo(_directory).EnumerateFileSystemInfosAsync(
            "*",
            new EnumerationOptions { MaxRecursionDepth = 0, RecurseSubdirectories = true });
        List<FileSystemInfo> items = new();

        await foreach (FileSystemInfo item in enumerable) {
            items.Add(item);
        }

        Assert.That(items.Exists(item => item.FullName == child), Is.True);
        Assert.That(items.Exists(item => item.FullName == nestedFile), Is.False);
    }

    [TestCase("*.*")]
    [TestCase("*.")]
    [TestCase("*.txt")]
    public async Task EnumerateFileSystemInfosAsync_Win32MatchType_MatchesFrameworkResults(string pattern) {
        Directory.CreateDirectory(Path.Combine(_directory, "folder"));
        File.WriteAllText(Path.Combine(_directory, "noext"), "item");
        File.WriteAllText(Path.Combine(_directory, "file.txt"), "item");
        EnumerationOptions options = new() { MatchType = MatchType.Win32 };
        List<string> expected = new DirectoryInfo(_directory)
            .EnumerateFileSystemInfos(pattern, options)
            .Select(item => item.Name)
            .Order(StringComparer.Ordinal)
            .ToList();
        DirectoryInfoAsyncEnumerable enumerable = new DirectoryInfo(_directory)
            .EnumerateFileSystemInfosAsync(pattern, options);
        List<string> actual = new();

        await foreach (FileSystemInfo item in enumerable) {
            actual.Add(item.Name);
        }

        Assert.That(actual.Order(StringComparer.Ordinal), Is.EqualTo(expected));
    }

    [Test]
    public async Task EnumerateFileSystemInfosAsync_ManyEntries_ReturnsEachEntryOnce() {
        var expected = new List<string>();
        for (var d = 0; d < 3; d++) {
            var directory = Path.Combine(_directory, $"folder{d}");
            Directory.CreateDirectory(directory);
            expected.Add(directory);
            for (var f = 0; f < 300; f++) {
                var file = Path.Combine(directory, $"file{f:D3}.txt");
                File.WriteAllText(file, "item");
                expected.Add(file);
            }
        }
        var enumerable = new DirectoryInfo(_directory).EnumerateFileSystemInfosAsync(
            "*",
            new EnumerationOptions { RecurseSubdirectories = true });
        var actual = new List<string>();

        await foreach (var item in enumerable) {
            actual.Add(item.FullName);
        }

        Assert.That(actual, Is.EquivalentTo(expected));
        Assert.That(enumerable.FileCount, Is.EqualTo(900));
        Assert.That(enumerable.DirectoryCount, Is.EqualTo(3));
    }

    [Test]
    public async Task EnumerateFileSystemInfosAsync_CanBeEnumeratedMoreThanOnce() {
        var first = Path.Combine(_directory, "first.txt");
        var second = Path.Combine(_directory, "second.txt");
        File.WriteAllText(first, "item");
        var enumerable = new DirectoryInfo(_directory).EnumerateFileSystemInfosAsync("*", new EnumerationOptions());

        var firstPass = new List<string>();
        await foreach (var item in enumerable) {
            firstPass.Add(item.FullName);
        }
        File.WriteAllText(second, "item");
        var secondPass = new List<string>();
        await foreach (var item in enumerable) {
            secondPass.Add(item.FullName);
        }

        Assert.That(firstPass, Is.EqualTo(new[] { first }));
        Assert.That(secondPass, Is.EquivalentTo(new[] { first, second }));
        Assert.That(enumerable.FileCount, Is.EqualTo(2));
    }

    [Test]
    public async Task EnumerateFileSystemInfosAsync_ConcurrentEnumeration_ThrowsInvalidOperationException() {
        File.WriteAllText(Path.Combine(_directory, "item.txt"), "item");
        var enumerable = new DirectoryInfo(_directory).EnumerateFileSystemInfosAsync("*", new EnumerationOptions());
        await using var first = enumerable.GetAsyncEnumerator(CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => enumerable.GetAsyncEnumerator(CancellationToken.None));
    }

    [Test]
    public async Task EnumerateFileSystemInfosAsync_Ready_FiresBeforeEnumerationCompletes() {
        File.WriteAllText(Path.Combine(_directory, "item.txt"), "item");
        DirectoryInfoAsyncEnumerable enumerable = new DirectoryInfo(_directory).EnumerateFileSystemInfosAsync(
            "*",
            new EnumerationOptions());
        bool ready = false;
        enumerable.Ready += (_, _) => ready = true;

        await foreach (FileSystemInfo item in enumerable) {
        }

        Assert.That(ready, Is.True);
    }

    [Test]
    public void EnumerateFileSystemInfosAsync_ReadyHandlerFailure_PropagatesToConsumer() {
        File.WriteAllText(Path.Combine(_directory, "item.txt"), "item");
        DirectoryInfoAsyncEnumerable enumerable = new DirectoryInfo(_directory)
            .EnumerateFileSystemInfosAsync("*", new EnumerationOptions());
        enumerable.Ready += (_, _) => throw new InvalidOperationException();

        Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await foreach (FileSystemInfo item in enumerable) {
            }
        });
    }

    [Test]
    public async Task Enumerating_SearchPattern_NotRaisedForUnmatchedFiles() {
        File.WriteAllText(Path.Combine(_directory, "match.txt"), "item");
        File.WriteAllText(Path.Combine(_directory, "skip.bin"), "item");
        var enumerable = new DirectoryInfo(_directory)
            .EnumerateFileSystemInfosAsync("*.txt", new EnumerationOptions());
        var raised = new ConcurrentBag<string>();
        enumerable.Enumerating += (_, args) => raised.Add(args.FileSystemInfo.Name);

        await foreach (var item in enumerable) {
        }

        Assert.That(raised, Is.EquivalentTo(new[] { "match.txt" }));
    }

    [Test]
    public async Task Enumerating_UnmatchedDirectoryNotSearched_NotRaised() {
        Directory.CreateDirectory(Path.Combine(_directory, "folder"));
        var enumerable = new DirectoryInfo(_directory)
            .EnumerateFileSystemInfosAsync("*.txt", new EnumerationOptions { RecurseSubdirectories = false });
        var raised = new ConcurrentBag<string>();
        enumerable.Enumerating += (_, args) => raised.Add(args.FileSystemInfo.Name);

        await foreach (var item in enumerable) {
        }

        Assert.That(raised, Is.Empty);
    }

    [Test]
    public async Task Enumerating_UnmatchedDirectoryAtMaxDepth_NotRaised() {
        Directory.CreateDirectory(Path.Combine(_directory, "one", "two"));
        var enumerable = new DirectoryInfo(_directory).EnumerateFileSystemInfosAsync(
            "*.txt",
            new EnumerationOptions { MaxRecursionDepth = 1, RecurseSubdirectories = true });
        var raised = new ConcurrentBag<string>();
        enumerable.Enumerating += (_, args) => raised.Add(args.FileSystemInfo.Name);

        await foreach (var item in enumerable) {
        }

        Assert.That(raised, Is.EquivalentTo(new[] { "one" }));
    }

    [Test]
    public async Task Enumerating_UnmatchedDirectorySearched_RaisedAndIgnorePreventsSearch() {
        var skipped = Path.Combine(_directory, "skipped");
        var searched = Path.Combine(_directory, "searched");
        Directory.CreateDirectory(skipped);
        Directory.CreateDirectory(searched);
        File.WriteAllText(Path.Combine(skipped, "hidden.txt"), "item");
        var expected = Path.Combine(searched, "found.txt");
        File.WriteAllText(expected, "item");
        var enumerable = new DirectoryInfo(_directory)
            .EnumerateFileSystemInfosAsync("*.txt", new EnumerationOptions { RecurseSubdirectories = true });
        var raised = new ConcurrentBag<string>();
        enumerable.Enumerating += (_, args) => {
            raised.Add(args.FileSystemInfo.Name);
            args.Ignore = args.FileSystemInfo.Name == "skipped";
        };
        var items = new List<string>();

        await foreach (var item in enumerable) {
            items.Add(item.FullName);
        }

        Assert.That(raised, Is.EquivalentTo(new[] { "skipped", "searched", "found.txt" }));
        Assert.That(items, Is.EqualTo(new[] { expected }));
        Assert.That(enumerable.FileCount, Is.EqualTo(1));
        Assert.That(enumerable.DirectoryCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Enumerating_IgnoredMatchingEntries_NotReturnedOrCounted() {
        Directory.CreateDirectory(Path.Combine(_directory, "folder"));
        File.WriteAllText(Path.Combine(_directory, "file.txt"), "item");
        var enumerable = new DirectoryInfo(_directory).EnumerateFileSystemInfosAsync("*", new EnumerationOptions());
        enumerable.Enumerating += (_, args) => args.Ignore = true;
        var items = new List<FileSystemInfo>();

        await foreach (var item in enumerable) {
            items.Add(item);
        }

        Assert.That(items, Is.Empty);
        Assert.That(enumerable.FileCount, Is.EqualTo(0));
        Assert.That(enumerable.DirectoryCount, Is.EqualTo(0));
    }

    [Test]
    public async Task EnumerateFileSystemInfosAsync_DisposedEarly_CancelsProducer() {
        string child = Path.Combine(_directory, "child");
        Directory.CreateDirectory(child);
        for (int i = 0; i < 2000; i++) {
            File.WriteAllText(Path.Combine(child, $"item-{i:D4}.txt"), "item");
        }
        TaskCompletionSource<bool> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim release = new(false);
        DirectoryInfoAsyncEnumerable enumerable = new DirectoryInfo(_directory).EnumerateFileSystemInfosAsync(
            "*",
            new EnumerationOptions { RecurseSubdirectories = true });
        int blocked = 0;
        enumerable.Enumerating += (_, args) => {
            if (args.FileSystemInfo is FileInfo && Interlocked.Exchange(ref blocked, 1) == 0) {
                entered.TrySetResult(true);
                release.Wait();
            }
        };
        enumerable.Ready += (_, _) => ready.TrySetResult(true);
        IAsyncEnumerator<FileSystemInfo> reader = enumerable.GetAsyncEnumerator(CancellationToken.None);
        Task disposed = null;
        try {
            Assert.That(await reader.MoveNextAsync(), Is.True);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            disposed = reader.DisposeAsync().AsTask();
            release.Set();
            await disposed.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally {
            release.Set();
            if (disposed == null) {
                await reader.DisposeAsync();
            }
            else {
                await disposed;
            }
        }

        Assert.That(ready.Task.IsCompleted, Is.False);
    }
}
