using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Threading.Tasks;

namespace Brows.IO.Helpers;

/// <summary>
/// Extension methods for getting the casing of a path as it is stored on disk.
/// </summary>
public static class FileSystemCasing {
    private static readonly EnumerationOptions EnumerationOptions = new() {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = false
    };

    private static string Correct(string path, CancellationToken cancellationToken) {
        var dir = new DirectoryInfo(path);
        var parts = new List<string>();
        var parent = dir.Parent;
        while (parent is not null) {
            if (cancellationToken.IsCancellationRequested) {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var name = dir.Name;
            var correctedName = name;
            try {
                var candidates = new FileSystemEnumerable<string>(
                    parent.FullName,
                    (ref FileSystemEntry entry) => entry.FileName.ToString(),
                    EnumerationOptions) {
                    ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                        entry.FileName.Equals(name, StringComparison.OrdinalIgnoreCase)
                };
                var match = default(string);
                foreach (string candidate in candidates) {
                    if (StringComparer.Ordinal.Equals(candidate, name)) {
                        match = candidate;
                        break;
                    }
                    match ??= candidate;
                }
                if (match != null) {
                    correctedName = match;
                }
            }
            catch (UnauthorizedAccessException) {
            }
            catch (DirectoryNotFoundException) {
            }
            parts.Add(correctedName);
            dir = parent;
            parent = dir.Parent;
        }
        string root = dir.FullName;
        if (OperatingSystem.IsWindows() && root.Length >= 2 && root[1] == ':' && char.IsAsciiLetter(root[0])) {
            root = char.ToUpperInvariant(root[0]) + root[1..];
        }
        parts.Add(root);
        parts.Reverse();
        return Path.Combine(parts.ToArray());
    }

    /// <summary>
    /// Gets the full path of a file or directory with each segment in the casing stored on disk.
    /// </summary>
    /// <remarks>
    /// Segments that do not exist or cannot be read keep their original casing. On Windows, the drive letter is
    /// returned in upper case.
    /// </remarks>
    /// <param name="fileSystemInfo">The file or directory whose path is corrected.</param>
    /// <returns>The corrected full path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileSystemInfo"/> is <see langword="null"/>.</exception>
    public static string CorrectCasing(this FileSystemInfo fileSystemInfo) {
        if (fileSystemInfo is null) {
            throw new ArgumentNullException(nameof(fileSystemInfo));
        }
        var path = fileSystemInfo.FullName;
        var correct = Correct(path, CancellationToken.None);
        return correct;
    }

    /// <summary>
    /// Gets the full path of a file or directory with each segment in the casing stored on disk, on a thread pool
    /// thread.
    /// </summary>
    /// <remarks>
    /// Segments that do not exist or cannot be read keep their original casing. On Windows, the drive letter is
    /// returned in upper case.
    /// </remarks>
    /// <param name="fileSystemInfo">The file or directory whose path is corrected.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task whose result is the corrected full path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileSystemInfo"/> is <see langword="null"/>.</exception>
    public static Task<string> CorrectCasingAsync(this FileSystemInfo fileSystemInfo,
                                                  CancellationToken cancellationToken) {
        if (fileSystemInfo is null) {
            throw new ArgumentNullException(nameof(fileSystemInfo));
        }
        var path = fileSystemInfo.FullName;
        var task = Task.Run(cancellationToken: cancellationToken, function: () => {
            var correct = Correct(path, cancellationToken);
            return correct;
        });
        return task;
    }
}
