using System.IO;

namespace Brows.IO.Helpers;

/// <summary>
/// Represents the method that handles the <see cref="DirectoryInfoAsyncEnumerable.Enumerating"/> event.
/// </summary>
/// <param name="sender">The enumerable that discovered the entry.</param>
/// <param name="e">The event data, which can be used to exclude the entry.</param>
public delegate void DirectoryInfoEnumeratingEventHandler(object sender, DirectoryInfoEnumeratingEventArgs e);

/// <summary>
/// Provides data for the <see cref="DirectoryInfoAsyncEnumerable.Enumerating"/> event.
/// </summary>
public sealed class DirectoryInfoEnumeratingEventArgs : EventArgs {
    /// <summary>
    /// Gets or sets a value indicating whether the entry is excluded from the results. Excluding a directory also
    /// prevents it from being searched.
    /// </summary>
    public bool Ignore { get; set; }

    /// <summary>
    /// Gets the discovered file or directory.
    /// </summary>
    public FileSystemInfo FileSystemInfo { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryInfoEnumeratingEventArgs"/> class.
    /// </summary>
    /// <param name="fileSystemInfo">The discovered file or directory.</param>
    public DirectoryInfoEnumeratingEventArgs(FileSystemInfo fileSystemInfo) {
        FileSystemInfo = fileSystemInfo;
    }
}