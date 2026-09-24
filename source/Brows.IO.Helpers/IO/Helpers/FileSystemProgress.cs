using System.IO;

namespace Brows.IO.Helpers;

/// <summary>
/// Receives progress reports from long-running filesystem operations.
/// </summary>
/// <remarks>
/// Members may be called from thread pool threads, but calls made by a single operation are not concurrent.
/// </remarks>
public abstract class FileSystemProgress {
    /// <summary>
    /// Adds to the total amount of work to be done.
    /// </summary>
    /// <param name="value">The number of items to add to the target.</param>
    public abstract void AddToTarget(long value);

    /// <summary>
    /// Adds to the amount of work that has been completed.
    /// </summary>
    /// <param name="value">The number of items that were completed.</param>
    public abstract void AddToProgress(long value);

    /// <summary>
    /// Reports the file or directory currently being processed.
    /// </summary>
    /// <param name="value">The file or directory being processed.</param>
    public abstract void SetCurrentInfo(FileSystemInfo value);
}