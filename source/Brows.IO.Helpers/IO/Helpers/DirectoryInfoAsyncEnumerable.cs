using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CHANNEL = System.Threading.Channels.Channel;

namespace Brows.IO.Helpers;

/// <summary>
/// Asynchronously enumerates the files and directories in a directory, searching subdirectories concurrently.
/// </summary>
/// <remarks>
/// Create instances with <see cref="DirectoryInfoExtension.EnumerateFileSystemInfosAsync"/>. Each instance can be
/// enumerated repeatedly, but only one traversal can be active at a time. The search pattern filters the returned
/// entries but not the directories that are searched. Entries are not returned in any particular order. Disposing
/// the enumerator stops the traversal.
/// </remarks>
public sealed class DirectoryInfoAsyncEnumerable : IAsyncEnumerable<FileSystemInfo> {
    private readonly SemaphoreSlim EnumerationLimit = new(Math.Max(1, Math.Min(8, Environment.ProcessorCount)));
    private Channel<FileSystemInfo> Channel;

    private bool Locked;

    private readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        Locker = new();

    private void CompleteEnumeration() {
        lock (Locker) {
            Locked = false;
        }
    }

    private static Channel<FileSystemInfo> CreateChannel() {
        return CHANNEL.CreateBounded<FileSystemInfo>(
            new BoundedChannelOptions(256) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
    }

    private bool Ignore(FileSystemInfo info) {
        DirectoryInfoEnumeratingEventHandler handler = Enumerating;
        if (handler != null) {
            var args = new DirectoryInfoEnumeratingEventArgs(info);
            handler(this, args);
            if (args.Ignore) {
                return true;
            }
        }
        return false;
    }

    private bool Searchable(DirectoryInfo directoryInfo) {
        if (EnumerateReparsePoints) {
            return true;
        }
        var attributes = directoryInfo.Attributes;
        return (int)attributes == -1 || attributes.HasFlag(FileAttributes.ReparsePoint) == false;
    }

    private async Task WriteFilesAndQueueDirectories(DirectoryInfo directoryInfo,
                                                      EnumerationOptions enumerationOptions,
                                                      bool searchChildren,
                                                      Queue<DiscoveredDirectory> queue,
                                                      CancellationToken cancellationToken) {
        await EnumerationLimit.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await Task.Run(cancellationToken: cancellationToken, function: async () => {
                var infos = directoryInfo.EnumerateFileSystemInfos("*", enumerationOptions);
                foreach (var info in infos) {
                    if (cancellationToken.IsCancellationRequested) {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    var match = FileSystemNameMatcher.Matches(SearchPattern, info.Name, enumerationOptions);
                    if (info is FileInfo file) {
                        if (match == false || Ignore(file)) {
                            continue;
                        }
                        await Channel.Writer.WriteAsync(file, cancellationToken).ConfigureAwait(false);
                        Interlocked.Add(ref _FileCount, 1);
                    }
                    else if (info is DirectoryInfo directory) {
                        var search = searchChildren && directory.Name is not ("." or "..") && Searchable(directory);
                        if ((match == false && search == false) || Ignore(directory)) {
                            continue;
                        }
                        queue.Enqueue(new DiscoveredDirectory(directory, match, search));
                        if (match) {
                            Interlocked.Add(ref _DirectoryCount, 1);
                        }
                    }
                }
            }).ConfigureAwait(false);
        }
        finally {
            EnumerationLimit.Release();
        }
    }

    private async Task WriteAll(CancellationToken cancellationToken) {
        var originalOptions = EnumerationOptions ?? new();
        var options = new EnumerationOptions() {
            AttributesToSkip = originalOptions.AttributesToSkip,
            BufferSize = originalOptions.BufferSize,
            IgnoreInaccessible = originalOptions.IgnoreInaccessible,
            MatchCasing = originalOptions.MatchCasing,
            MatchType = originalOptions.MatchType,
            MaxRecursionDepth = 0,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = originalOptions.ReturnSpecialDirectories
        };
        var recurse = originalOptions.RecurseSubdirectories;
        var maxDepth = originalOptions.MaxRecursionDepth;
        async Task continuation(Queue<DiscoveredDirectory> queue, int depth) {
            var tasks = new List<Task>();
            while (queue.TryDequeue(out var discovered)) {
                if (discovered.Match) {
                    await Channel.Writer.WriteAsync(discovered.Info, cancellationToken).ConfigureAwait(false);
                }
                if (discovered.Search) {
                    var children = new Queue<DiscoveredDirectory>();
                    async Task enumerateChildren() {
                        await WriteFilesAndQueueDirectories(discovered.Info,
                                                            options,
                                                            recurse && depth + 1 <= maxDepth,
                                                            children,
                                                            cancellationToken).ConfigureAwait(false);
                        await continuation(children, depth + 1).ConfigureAwait(false);
                    }
                    tasks.Add(enumerateChildren());
                }
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        if (Searchable(DirectoryInfo) == false) {
            return;
        }
        var rootDirectories = new Queue<DiscoveredDirectory>();
        await WriteFilesAndQueueDirectories(DirectoryInfo, options, recurse && 1 <= maxDepth, rootDirectories,
                                            cancellationToken).ConfigureAwait(false);
        await continuation(rootDirectories, 1).ConfigureAwait(false);
    }
    private async Task Produce(CancellationToken cancellationToken) {
        var error = default(Exception);
        try {
            await WriteAll(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Ready?.Invoke(this, new DirectoryInfoReadyEventArgs());
        }
        catch (Exception exception) {
            error = exception;
        }
        Channel.Writer.TryComplete(error);
    }

    internal DirectoryInfo DirectoryInfo { get; }
    internal string SearchPattern { get; }
    internal EnumerationOptions EnumerationOptions { get; }

    internal DirectoryInfoAsyncEnumerable(DirectoryInfo directoryInfo,
                                          string searchPattern,
                                          EnumerationOptions enumerationOptions) {
        DirectoryInfo = directoryInfo;
        SearchPattern = searchPattern;
        EnumerationOptions = enumerationOptions;
        Channel = CreateChannel();
    }

    /// <summary>Raised after a successful traversal and before the asynchronous sequence completes.</summary>
    public event DirectoryInfoReadyEventHandler Ready;

    /// <summary>
    /// Raised for each discovered entry that will be returned or searched, before it is returned or searched.
    /// Entries whose names do not match the search pattern are included only if they are directories that will be
    /// searched. Handlers may run concurrently on enumeration worker threads.
    /// </summary>
    public event DirectoryInfoEnumeratingEventHandler Enumerating;

    /// <summary>
    /// Gets the number of directories matching the search pattern that have been discovered. The value is final
    /// when <see cref="Ready"/> is raised, and is reset when the next traversal starts.
    /// </summary>
    public long DirectoryCount => Interlocked.Read(ref _DirectoryCount);
    private long _DirectoryCount;

    /// <summary>
    /// Gets the number of files matching the search pattern that have been discovered. The value is final when
    /// <see cref="Ready"/> is raised, and is reset when the next traversal starts.
    /// </summary>
    public long FileCount => Interlocked.Read(ref _FileCount);
    private long _FileCount;

    /// <summary>
    /// Gets or sets a value indicating whether directories that are reparse points, such as symbolic links and
    /// junctions, are searched. The default is <see langword="false"/>. Set this before enumeration starts.
    /// </summary>
    public bool EnumerateReparsePoints { get; set; }

    /// <summary>
    /// Starts a traversal and returns an enumerator over the discovered entries.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the traversal.</param>
    /// <returns>An enumerator over the discovered files and directories.</returns>
    /// <exception cref="InvalidOperationException">Another traversal is active.</exception>
    public IAsyncEnumerator<FileSystemInfo> GetAsyncEnumerator(CancellationToken cancellationToken) {
        lock (Locker) {
            if (Locked) {
                throw new InvalidOperationException("This directory enumeration already has an active traversal.");
            }
            Locked = true;
            Interlocked.Exchange(ref _DirectoryCount, 0);
            Interlocked.Exchange(ref _FileCount, 0);
            Channel = CreateChannel();
        }
        var producerCancellation = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        var producer = Produce(producerCancellation.Token);
        var reader = Channel.Reader
            .ReadAllAsync(producerCancellation.Token)
            .GetAsyncEnumerator(producerCancellation.Token);
        return new ProducerEnumerator(reader, producerCancellation, producer, CompleteEnumeration);
    }

    private readonly record struct DiscoveredDirectory(DirectoryInfo Info, bool Match, bool Search);

    private sealed class ProducerEnumerator : IAsyncEnumerator<FileSystemInfo> {
        private readonly IAsyncEnumerator<FileSystemInfo> Reader;
        private readonly CancellationTokenSource Cancellation;
        private readonly Task Producer;
        private readonly Action Completion;

        private bool Disposed;

        public FileSystemInfo Current => Reader.Current;

        public ProducerEnumerator(IAsyncEnumerator<FileSystemInfo> reader,
                                  CancellationTokenSource cancellation,
                                  Task producer,
                                  Action completion) {
            Reader = reader;
            Cancellation = cancellation;
            Producer = producer;
            Completion = completion;
        }

        public ValueTask<bool> MoveNextAsync() => Reader.MoveNextAsync();

        public async ValueTask DisposeAsync() {
            if (Disposed) {
                return;
            }
            Disposed = true;
            Cancellation.Cancel();
            try {
                await Reader.DisposeAsync().ConfigureAwait(false);
                await Producer.ConfigureAwait(false);
            }
            finally {
                Cancellation.Dispose();
                Completion();
            }
        }
    }
}
