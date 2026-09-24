using System.Collections.Generic;
using System.IO;

namespace Brows.IO.Helpers;

/// <summary>
/// Extension methods for enumerating directories.
/// </summary>
public static class DirectoryInfoExtension {
    private static IEnumerable<FileSystemInfo>
    EnumerateFilesAndAddDirectories(DirectoryInfo directoryInfo,
                                    string searchPattern,
                                    EnumerationOptions enumerationOptions,
                                    ICollection<DirectoryInfo> directories) {
        var infos = directoryInfo.EnumerateFileSystemInfos("*", enumerationOptions);
        foreach (var info in infos) {
            if (info is FileInfo file) {
                if (FileSystemNameMatcher.Matches(searchPattern, file.Name, enumerationOptions)) {
                    yield return file;
                }
            }
            else if (info is DirectoryInfo directory) {
                directories.Add(directory);
            }
        }
    }

    private static IEnumerable<FileSystemInfo> EnumerateBreadthFirst(DirectoryInfo directoryInfo,
                                                                     string searchPattern,
                                                                     EnumerationOptions enumerationOptions,
                                                                     bool recurse) {
        var directories = new List<DirectoryInfo>();
        foreach (var item in EnumerateFilesAndAddDirectories(directoryInfo,
                                                             searchPattern,
                                                             enumerationOptions,
                                                             directories)) {
            yield return item;
        }
        int depth = 1;
        while (directories.Count > 0) {
            var nextDepth = new List<DirectoryInfo>();
            var directoriesToEnumerate = new List<DirectoryInfo>();
            foreach (var directory in directories) {
                if (FileSystemNameMatcher.Matches(searchPattern, directory.Name, enumerationOptions)) {
                    yield return directory;
                }
                if (directory.Name is "." or "..") {
                    continue;
                }
                var attributes = directory.Attributes;
                if ((int)attributes != -1 && attributes.HasFlag(FileAttributes.ReparsePoint)) {
                    continue;
                }
                if (recurse && depth <= enumerationOptions.MaxRecursionDepth) {
                    directoriesToEnumerate.Add(directory);
                }
            }
            foreach (var directory in directoriesToEnumerate) {
                foreach (var info in EnumerateFilesAndAddDirectories(directory,
                                                                     searchPattern,
                                                                     enumerationOptions,
                                                                     nextDepth)) {
                    yield return info;
                }
            }
            directories = nextDepth;
            depth++;
        }
    }

    /// <summary>
    /// Enumerates the files and directories in a directory, returning all entries at one depth before searching
    /// the next.
    /// </summary>
    /// <remarks>
    /// Subdirectories are searched when <see cref="EnumerationOptions.RecurseSubdirectories"/> is
    /// <see langword="true"/>, up to <see cref="EnumerationOptions.MaxRecursionDepth"/>. Reparse points and the
    /// special <c>.</c> and <c>..</c> directories are returned but not searched. The search pattern filters the
    /// returned entries but not the directories that are searched.
    /// </remarks>
    /// <param name="directoryInfo">The directory to search.</param>
    /// <param name="searchPattern">The pattern that returned file and directory names must match.</param>
    /// <param name="enumerationOptions">
    /// The options that control matching, recursion, skipped attributes and depth, or <see langword="null"/> to
    /// search all subdirectories with the default options.
    /// </param>
    /// <returns>A lazily evaluated sequence of the matching files and directories.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="directoryInfo"/> or <paramref name="searchPattern"/> is <see langword="null"/>.
    /// </exception>
    public static IEnumerable<FileSystemInfo>
    EnumerateFileSystemInfosBreadthFirst(this DirectoryInfo directoryInfo,
                                         string searchPattern,
                                         EnumerationOptions enumerationOptions) {
        if (directoryInfo is null) {
            throw new ArgumentNullException(nameof(directoryInfo));
        }
        if (searchPattern is null) {
            throw new ArgumentNullException(nameof(searchPattern));
        }
        var originalOptions = enumerationOptions ?? new() { RecurseSubdirectories = true };
        var options = new EnumerationOptions() {
            AttributesToSkip = originalOptions.AttributesToSkip,
            BufferSize = originalOptions.BufferSize,
            IgnoreInaccessible = originalOptions.IgnoreInaccessible,
            MatchCasing = originalOptions.MatchCasing,
            MatchType = originalOptions.MatchType,
            MaxRecursionDepth = originalOptions.MaxRecursionDepth,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = originalOptions.ReturnSpecialDirectories
        };
        return EnumerateBreadthFirst(directoryInfo, searchPattern, options, originalOptions.RecurseSubdirectories);
    }

    /// <summary>
    /// Creates an asynchronous sequence of the files and directories in a directory.
    /// </summary>
    /// <remarks>
    /// Nothing is read from the filesystem until the sequence is enumerated. Subdirectories are searched when
    /// <see cref="EnumerationOptions.RecurseSubdirectories"/> is <see langword="true"/>.
    /// </remarks>
    /// <param name="directoryInfo">The directory to search.</param>
    /// <param name="searchPattern">The pattern that returned file and directory names must match.</param>
    /// <param name="enumerationOptions">
    /// The options that control matching, recursion and skipped attributes, or <see langword="null"/> for the
    /// defaults.
    /// </param>
    /// <returns>A sequence that can be enumerated repeatedly, but not concurrently.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="directoryInfo"/> or <paramref name="searchPattern"/> is <see langword="null"/>.
    /// </exception>
    public static DirectoryInfoAsyncEnumerable
    EnumerateFileSystemInfosAsync(this DirectoryInfo directoryInfo,
                                  string searchPattern,
                                  EnumerationOptions enumerationOptions) {
        if (directoryInfo is null) {
            throw new ArgumentNullException(nameof(directoryInfo));
        }
        if (searchPattern is null) {
            throw new ArgumentNullException(nameof(searchPattern));
        }
        return new DirectoryInfoAsyncEnumerable(directoryInfo, searchPattern, enumerationOptions);
    }
}
