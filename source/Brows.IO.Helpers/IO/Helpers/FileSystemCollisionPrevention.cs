using System.IO;

namespace Brows.IO.Helpers;

/// <summary>
/// Chooses alternative names for paths that are already in use.
/// </summary>
public abstract class FileSystemCollisionPrevention {
    /// <summary>
    /// Gets the default implementation, which inserts the attempt number before the extension, so that the first
    /// attempt for <c>file.txt</c> is <c>file (1).txt</c>. Numbers already in the name are kept, so the first
    /// attempt for <c>Movie (2020).mp4</c> is <c>Movie (2020) (1).mp4</c>. Directories and names that start with
    /// a single dot, such as <c>.gitignore</c>, get the number after the whole name.
    /// </summary>
    /// <remarks>This implementation does not access the filesystem.</remarks>
    public static FileSystemCollisionPrevention Default { get; } = new DefaultImplementation();

    /// <summary>
    /// Gets a candidate replacement for a path that is already in use.
    /// </summary>
    /// <param name="path">The original path that is in use. It is the same for every attempt.</param>
    /// <param name="isDirectory">
    /// <see langword="true"/> if <paramref name="path"/> is a directory; <see langword="false"/> if it is a file.
    /// </param>
    /// <param name="attempt">The attempt number, starting at 1.</param>
    /// <returns>A different path to try. It is not guaranteed to be unused.</returns>
    public abstract string Rename(string path, bool isDirectory, int attempt);

    private sealed class DefaultImplementation : FileSystemCollisionPrevention {
        public sealed override string Rename(string path, bool isDirectory, int attempt) {
            if (path is null) {
                throw new ArgumentNullException(nameof(path));
            }
            if (attempt < 1) {
                throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "The attempt must be at least 1.");
            }
            var normalizedPath = Path.TrimEndingDirectorySeparator(path);
            var fileName = Path.GetFileName(normalizedPath);
            if (fileName.Length == 0) {
                throw new ArgumentException("A filesystem root cannot be renamed.", nameof(path));
            }
            var directory = Path.GetDirectoryName(normalizedPath);
            var extension = isDirectory ? string.Empty : Path.GetExtension(fileName);
            if (extension.Length == fileName.Length) {
                extension = string.Empty;
            }
            var name = fileName[..^extension.Length];
            var renamedName = $"{name} ({attempt}){extension}";
            return directory is null ? renamedName : Path.Combine(directory, renamedName);
        }
    }
}