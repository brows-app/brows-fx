using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Brows.IO.Helpers;

/// <summary>
/// Asynchronous filesystem operations that run on the thread pool.
/// </summary>
public static class FileSystemTask {
    /// <summary>
    /// Gets a file if it exists.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>
    /// A task whose result is the file, or <see langword="null"/> if <paramref name="path"/> is
    /// <see langword="null"/> or invalid, or no file exists at the path.
    /// </returns>
    public static Task<FileInfo> ExistingFile(string path, CancellationToken cancellationToken) {
        if (path is null) {
            return Task.FromResult<FileInfo>(null);
        }
        return Task.Run(cancellationToken: cancellationToken, function: () => {
            var file = default(FileInfo);
            try {
                file = new FileInfo(path);
            }
            catch (ArgumentException) {
                return null;
            }
            catch (NotSupportedException) {
                return null;
            }
            catch (PathTooLongException) {
                return null;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return file.Exists ? file : null;
        });
    }

    /// <summary>
    /// Gets a directory if it exists.
    /// </summary>
    /// <param name="path">The path of the directory.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>
    /// A task whose result is the directory, or <see langword="null"/> if <paramref name="path"/> is
    /// <see langword="null"/> or invalid, or no directory exists at the path.
    /// </returns>
    public static Task<DirectoryInfo> ExistingDirectory(string path, CancellationToken cancellationToken) {
        if (path is null) {
            return Task.FromResult<DirectoryInfo>(null);
        }
        return Task.Run(cancellationToken: cancellationToken, function: () => {
            var directory = default(DirectoryInfo);
            try {
                directory = new DirectoryInfo(path);
            }
            catch (ArgumentException) {
                return null;
            }
            catch (NotSupportedException) {
                return null;
            }
            catch (PathTooLongException) {
                return null;
            }
            if (cancellationToken.IsCancellationRequested) {
                cancellationToken.ThrowIfCancellationRequested();
            }
            return directory.Exists ? directory : null;
        });
    }

    /// <summary>
    /// Gets the file or directory at a path if it exists.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>
    /// A task whose result is a <see cref="FileInfo"/> or <see cref="DirectoryInfo"/>, or <see langword="null"/>
    /// if <paramref name="path"/> is <see langword="null"/> or invalid, or nothing at the path exists or can be
    /// read.
    /// </returns>
    public static Task<FileSystemInfo> Existing(string path, CancellationToken cancellationToken) {
        if (path is null) {
            return Task.FromResult<FileSystemInfo>(null);
        }
        return Task.Run<FileSystemInfo>(cancellationToken: cancellationToken, function: () => {
            if (cancellationToken.IsCancellationRequested) {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var file = default(FileInfo);
            try {
                file = new FileInfo(path);
            }
            catch (ArgumentException) {
                return null;
            }
            catch (NotSupportedException) {
                return null;
            }
            catch (PathTooLongException) {
                return null;
            }
            var attributes = default(FileAttributes);
            try {
                attributes = file.Attributes;
            }
            catch (IOException) {
                return null;
            }
            catch (UnauthorizedAccessException) {
                return null;
            }
            if ((int)attributes == -1) {
                return null;
            }
            if (attributes.HasFlag(FileAttributes.Directory)) {
                return new DirectoryInfo(path);
            }
            return file;
        });
    }

    /// <summary>
    /// Gets a path that is not in use, starting with the given path.
    /// </summary>
    /// <remarks>
    /// If <paramref name="path"/> is in use, <see cref="FileSystemCollisionPrevention.Rename"/> is called with the
    /// original path, whether it is a directory, and attempt numbers starting at 1 until an unused candidate is
    /// found. Another process can take the returned path before it is used.
    /// </remarks>
    /// <param name="path">The preferred path.</param>
    /// <param name="collision">
    /// The strategy that chooses candidates, or <see langword="null"/> for
    /// <see cref="FileSystemCollisionPrevention.Default"/>.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task whose result is <paramref name="path"/> or the first unused candidate.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// The strategy returned <see langword="null"/> or a path it already returned, or no unused path was found
    /// after 10,000 attempts.
    /// </exception>
    public static Task<string> Nonexistent(string path,
                                           FileSystemCollisionPrevention collision,
                                           CancellationToken cancellationToken) {
        if (string.IsNullOrEmpty(path)) {
            throw new ArgumentNullException(nameof(path));
        }
        collision = collision ?? FileSystemCollisionPrevention.Default;
        return Task.Run(cancellationToken: cancellationToken, function: () => {
            if (cancellationToken.IsCancellationRequested) {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var isDirectory = Directory.Exists(path);
            if (isDirectory == false && File.Exists(path) == false) {
                return path;
            }
            var attempted = new HashSet<string>(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal) {
                path
            };
            const int maxAttempts = 10000;
            for (var attempt = 1; attempt <= maxAttempts; attempt++) {
                if (cancellationToken.IsCancellationRequested) {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                var candidate = collision.Rename(path, isDirectory, attempt) ??
                    throw new InvalidOperationException("Collision handling returned a null path.");
                if (attempted.Add(candidate) == false) {
                    throw new InvalidOperationException($"Collision handling repeated the path '{candidate}'.");
                }
                if (Path.Exists(candidate) == false) {
                    return candidate;
                }
            }
            throw new InvalidOperationException($"Could not find an unused path after {maxAttempts} attempts.");
        });
    }

    /// <summary>
    /// Deletes a file, or a directory and everything in it.
    /// </summary>
    /// <remarks>
    /// Read-only entries are deleted. Links inside a directory are deleted without deleting their targets. A
    /// missing file is not an error. For a directory, the progress target is the number of files and directories
    /// in it plus one for the directory itself; for a file, it is one.
    /// </remarks>
    /// <param name="fileSystemInfo">The file or directory to delete.</param>
    /// <param name="fileSystemProgress">The object that receives progress, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when the entry is deleted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileSystemInfo"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="fileSystemInfo"/> is neither a <see cref="FileInfo"/> nor a <see cref="DirectoryInfo"/>.
    /// </exception>
    public static async Task Delete(FileSystemInfo fileSystemInfo,
                                    FileSystemProgress fileSystemProgress,
                                    CancellationToken cancellationToken) {
        if (fileSystemInfo is null) {
            throw new ArgumentNullException(nameof(fileSystemInfo));
        }
        if (fileSystemInfo is DirectoryInfo directory) {
            await new DirectoryDeleter(directory)
                .Delete(fileSystemProgress, cancellationToken);
            return;
        }
        if (fileSystemInfo is FileInfo file) {
            fileSystemProgress?.SetCurrentInfo(file);
            fileSystemProgress?.AddToTarget(1);
            await FileDeleter
                .Delete(file, cancellationToken);
            fileSystemProgress?.AddToProgress(1);
            return;
        }
        throw new ArgumentException("The filesystem entry must be a file or directory.", nameof(fileSystemInfo));
    }

    /// <summary>
    /// Creates a directory and any missing parent directories.
    /// </summary>
    /// <param name="path">The path of the directory.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task whose result is the directory. It is not an error if the directory already exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static Task<DirectoryInfo> CreateDirectory(string path, CancellationToken cancellationToken) {
        if (path is null) {
            throw new ArgumentNullException(nameof(path));
        }
        return Task.Run(cancellationToken: cancellationToken, function: () => {
            return Directory.CreateDirectory(path);
        });
    }
}
