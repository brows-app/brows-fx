using Domore.Logs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using DIRECTORY = System.IO.Directory;

namespace Brows.IO.Helpers;

internal sealed class DirectoryDeleter {
    private static readonly ILog Log = Logging.For(typeof(DirectoryDeleter));

    private static Task Delete(DirectoryInfo directory, CancellationToken cancellationToken) {
        if (directory is null) {
            throw new ArgumentNullException(nameof(directory));
        }
        if (Log.Debug()) {
            Log.Debug(nameof(Delete) + " > " + directory.FullName);
        }
        return Task.Run(cancellationToken: cancellationToken, action: () => {
            try {
                if (DIRECTORY.Exists(directory.FullName) == false) {
                    return;
                }
                try {
                    directory.Delete(recursive: true);
                }
                catch (DirectoryNotFoundException) {
                }
                catch (UnauthorizedAccessException exception) {
                    if (Log.Info()) {
                        Log.Info(nameof(UnauthorizedAccessException) + " > " + directory.FullName, exception);
                    }
                    deleteAfterReset(directory);
                }
                catch (IOException exception) {
                    if (Log.Info()) {
                        Log.Info(exception);
                    }
                    deleteAfterReset(directory);
                }
            }
            catch (DirectoryNotFoundException) {
            }
            static void deleteAfterReset(DirectoryInfo directory) {
                directory.Attributes = FileAttributes.Normal;
                directory.Refresh();
                directory.Delete(recursive: true);
            }
        });
    }

    public DirectoryInfo Directory { get; }

    public DirectoryDeleter(DirectoryInfo directory) {
        Directory = directory ?? throw new ArgumentNullException(nameof(directory));
    }

    public async Task Delete(FileSystemProgress progress, CancellationToken cancellationToken) {
        var fileTasks = new List<Task>();
        var directories = new List<DirectoryInfo>();
        var progressLock = new object();
        var targetReady = false;
        var pendingProgress = 0L;
        using var deleteLimit = new SemaphoreSlim(Math.Max(1, Math.Min(8, Environment.ProcessorCount)));
        void setCurrentInfo(FileSystemInfo value) {
            lock (progressLock) {
                progress?.SetCurrentInfo(value);
            }
        }
        void addToProgress(long value) {
            if (progress == null) {
                return;
            }
            lock (progressLock) {
                if (targetReady) {
                    progress?.AddToProgress(value);
                }
                else {
                    pendingProgress += value;
                }
            }
        }
        var enumerable = Directory.EnumerateFileSystemInfosAsync("*", new EnumerationOptions {
            AttributesToSkip = 0,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false
        });
        enumerable.Ready += (_, _) => {
            lock (progressLock) {
                progress?.AddToTarget(enumerable.FileCount + enumerable.DirectoryCount + 1);
                targetReady = true;
                if (pendingProgress != 0) {
                    progress?.AddToProgress(pendingProgress);
                    pendingProgress = 0;
                }
            }
        };
        async Task deleteFile(FileInfo file) {
            try {
                setCurrentInfo(file);
                await FileDeleter.Delete(file, cancellationToken).ConfigureAwait(false);
                addToProgress(1);
            }
            finally {
                deleteLimit.Release();
            }
        }
        var enumerationError = default(Exception);
        try {
            await foreach (FileSystemInfo item in enumerable.WithCancellation(cancellationToken)) {
                cancellationToken.ThrowIfCancellationRequested();
                if (item is FileInfo file) {
                    await deleteLimit.WaitAsync(cancellationToken).ConfigureAwait(false);
                    fileTasks.Add(deleteFile(file));
                    if (fileTasks.Count >= 1024) {
                        fileTasks.RemoveAll(task => task.IsCompletedSuccessfully);
                    }
                }
                else if (item is DirectoryInfo directory) {
                    directories.Add(directory);
                }
            }
        }
        catch (Exception exception) {
            enumerationError = exception;
        }
        var deleteError = default(Exception);
        try {
            await Task.WhenAll(fileTasks).ConfigureAwait(false);
        }
        catch (Exception exception) {
            deleteError = exception;
        }
        if (enumerationError != null) {
            if (enumerationError is OperationCanceledException && deleteError is OperationCanceledException) {
                ExceptionDispatchInfo.Capture(enumerationError).Throw();
            }
            if (deleteError != null) {
                throw new AggregateException(enumerationError, deleteError);
            }
            ExceptionDispatchInfo.Capture(enumerationError).Throw();
        }
        if (deleteError != null) {
            ExceptionDispatchInfo.Capture(deleteError).Throw();
        }
        setCurrentInfo(Directory);
        try {
            await Delete(Directory, cancellationToken).ConfigureAwait(false);
            addToProgress(directories.Count + 1);
            return;
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception exception) {
            if (Log.Info()) {
                Log.Info(exception);
            }
        }
        var fallbackErrors = new List<Exception>();
        foreach (var directory in directories.OrderByDescending(item => item.FullName.Length)) {
            cancellationToken.ThrowIfCancellationRequested();
            setCurrentInfo(directory);
            try {
                await Delete(directory, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch (Exception exception) {
                fallbackErrors.Add(exception);
            }
            addToProgress(1);
        }
        setCurrentInfo(Directory);
        try {
            await Delete(Directory, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception exception) {
            fallbackErrors.Add(exception);
            throw new AggregateException(fallbackErrors);
        }
        addToProgress(1);
    }
}
