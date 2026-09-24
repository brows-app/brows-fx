namespace Brows.IO.Helpers;

/// <summary>
/// Represents the method that handles the <see cref="DirectoryInfoAsyncEnumerable.Ready"/> event.
/// </summary>
/// <param name="sender">The enumerable that finished its traversal.</param>
/// <param name="e">The event data.</param>
public delegate void DirectoryInfoReadyEventHandler(object sender, DirectoryInfoReadyEventArgs e);

/// <summary>
/// Provides data for the <see cref="DirectoryInfoAsyncEnumerable.Ready"/> event.
/// </summary>
public sealed class DirectoryInfoReadyEventArgs : EventArgs {
}