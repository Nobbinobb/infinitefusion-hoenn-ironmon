namespace Ironmon.Tracker.Connection;

/// <summary>
/// Writes tracker-owned text files through unique same-directory temporary files.
/// </summary>
internal static class TrackerAtomicFileWriter
{
    /// <summary>
    /// Atomically writes complete text and removes its temporary file after a failed replacement.
    /// </summary>
    /// <param name="path">The final tracker-owned file path.</param>
    /// <param name="contents">The complete file contents.</param>
    internal static void WriteAllText(string path, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);
        string temporaryPath = CreateTemporaryPath(path);
        try
        {
            Directory.CreateDirectory(GetDirectory(path));
            File.WriteAllText(temporaryPath, contents);
            File.Move(temporaryPath, path, true);
        }
        catch
        {
            DeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    /// <summary>
    /// Atomically writes complete text asynchronously and removes its temporary file after a failed replacement.
    /// </summary>
    /// <param name="path">The final tracker-owned file path.</param>
    /// <param name="contents">The complete file contents.</param>
    /// <param name="cancellationToken">The token that cancels the temporary-file write.</param>
    /// <returns>A task representing the atomic write.</returns>
    internal static async Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);
        string temporaryPath = CreateTemporaryPath(path);
        try
        {
            Directory.CreateDirectory(GetDirectory(path));
            await File.WriteAllTextAsync(temporaryPath, contents, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, true);
        }
        catch
        {
            DeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    /// <summary>
    /// Creates a collision-resistant temporary path beside the final file.
    /// </summary>
    /// <param name="path">The final file path.</param>
    /// <returns>The unique temporary path.</returns>
    private static string CreateTemporaryPath(string path)
    {
        string directory = GetDirectory(path);
        string fileName = Path.GetFileName(path);
        return Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}{TrackerStorageNames.TemporaryExtension}");
    }

    /// <summary>
    /// Gets the required parent directory of a tracker-owned file.
    /// </summary>
    /// <param name="path">The final file path.</param>
    /// <returns>The parent directory.</returns>
    /// <exception cref="IOException">Thrown when the file path has no parent directory.</exception>
    private static string GetDirectory(string path)
        => Path.GetDirectoryName(path) ?? throw new IOException("The tracker-owned file path has no parent directory.");

    /// <summary>
    /// Best-effort removes a temporary file without replacing the original write failure.
    /// </summary>
    /// <param name="temporaryPath">The temporary file path.</param>
    private static void DeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
