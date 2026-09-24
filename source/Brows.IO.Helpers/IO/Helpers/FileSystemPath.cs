using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Brows.IO.Helpers;

/// <summary>
/// Helpers for finding the common parent of a set of paths.
/// </summary>
public static class FileSystemPath {
    private static bool IsDriveSegment(string segment) {
        return Path.VolumeSeparatorChar != Path.DirectorySeparatorChar &&
            segment.Length == 2 &&
            segment[1] == Path.VolumeSeparatorChar &&
            char.IsAsciiLetter(segment[0]);
    }

    /// <summary>
    /// Gets the longest parent path shared by all of the given paths.
    /// </summary>
    /// <remarks>
    /// Segments are compared whole, so <c>C:\ab</c> and <c>C:\abc</c> share only <c>C:\</c>. Null paths and
    /// duplicates are ignored.
    /// </remarks>
    /// <param name="paths">The paths to compare.</param>
    /// <param name="comparer">The comparer used to compare roots and segments.</param>
    /// <returns>
    /// The common path, or an empty string when there are no paths or the paths have different roots.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="paths"/> or <paramref name="comparer"/> is <see langword="null"/>.
    /// </exception>
    public static string CommonOf(IEnumerable<string> paths, StringComparer comparer) {
        if (paths is null) {
            throw new ArgumentNullException(nameof(paths));
        }
        if (comparer is null) {
            throw new ArgumentNullException(nameof(comparer));
        }
        var items = paths
            .Where(path => path is not null)
            .Distinct(comparer)
            .Select(path => {
                string root = Path.GetPathRoot(path) ?? string.Empty;
                return (Root: root, Parts: path[root.Length..].Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries));
            })
            .OrderBy(item => item.Parts.Length)
            .ToList();
        if (items.Count == 0) {
            return string.Empty;
        }
        var commonRoot = items[0].Root;
        var commonParts = items[0].Parts.ToList();
        foreach ((var Root, var Parts) in items) {
            if (comparer.Equals(commonRoot, Root) == false) {
                return string.Empty;
            }
            var i = 0;
            while (i < commonParts.Count && i < Parts.Length &&
                   comparer.Equals(commonParts[i], Parts[i])) {
                i++;
            }
            if (i < commonParts.Count) {
                commonParts.RemoveRange(i, commonParts.Count - i);
            }
        }
        var relative = string.Join(Path.DirectorySeparatorChar, commonParts);
        if (commonRoot.Length == 0) {
            return relative;
        }
        return relative.Length == 0 ? commonRoot : Path.Combine(commonRoot, relative);
    }

    /// <summary>
    /// Gets each path relative to the common parent of all of the paths, for example to build archive entry names.
    /// </summary>
    /// <remarks>
    /// For a single path, the common parent is its parent directory. When the paths have no common parent, the
    /// full paths are returned without the volume separator, so <c>C:\a.txt</c> becomes <c>C\a.txt</c>. Null
    /// paths and duplicates are ignored. Relative paths use <see cref="Path.DirectorySeparatorChar"/>.
    /// </remarks>
    /// <param name="paths">The paths to make relative.</param>
    /// <param name="comparer">The comparer used to compare roots and segments.</param>
    /// <param name="backtrack">
    /// The number of trailing segments of the common parent to keep in each relative path. Values larger than the
    /// number of segments keep the whole path.
    /// </param>
    /// <returns>Each distinct path paired with its relative path.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="paths"/> or <paramref name="comparer"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="backtrack"/> is negative.</exception>
    public static IEnumerable<(string OriginalPath, string RelativePath)>
    SkipCommonOf(IEnumerable<string> paths, StringComparer comparer, int backtrack = 0) {
        if (paths is null) {
            throw new ArgumentNullException(nameof(paths));
        }
        if (comparer is null) {
            throw new ArgumentNullException(nameof(comparer));
        }
        if (backtrack < 0) {
            throw new ArgumentOutOfRangeException(paramName: nameof(backtrack));
        }
        var list = paths.Where(path => path is not null).Distinct(comparer).ToList();
        if (list.Count == 0) {
            return Array.Empty<(string OriginalPath, string RelativePath)>();
        }
        var common = default(string);
        if (list.Count == 1) {
            var normalized = Path.TrimEndingDirectorySeparator(list[0]);
            common = Path.GetDirectoryName(normalized) ?? string.Empty;
        }
        else {
            common = CommonOf(list, comparer);
        }
        var commonCount = common.Length == 0
            ? 0
            : common.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries).Length;
        var skipCount = Math.Max(0, commonCount - backtrack);
        return list
            .Select(path => {
                var originalPath = path;
                var segments = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries);
                if (skipCount == 0 && segments.Length > 0 && IsDriveSegment(segments[0])) {
                    segments[0] = segments[0][..^1];
                }
                var relativePath = string.Join(Path.DirectorySeparatorChar, segments.Skip(skipCount));
                return (originalPath, relativePath);
            });
    }
}
