namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies shared atomic tracker-owned file replacement and cleanup.
/// </summary>
public sealed class TrackerAtomicFileWriterTests
{
    /// <summary>
    /// Initializes atomic file writer tests.
    /// </summary>
    public TrackerAtomicFileWriterTests()
    {
    }

    /// <summary>
    /// Verifies synchronous and asynchronous writes replace the final file without temporary residue.
    /// </summary>
    [Fact]
    public async Task WriterAtomicallyReplacesCompleteText()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "state.json");

            TrackerAtomicFileWriter.WriteAllText(path, "first");
            Assert.Equal("first", File.ReadAllText(path));
            await TrackerAtomicFileWriter.WriteAllTextAsync(path, "second");

            Assert.Equal("second", File.ReadAllText(path));
            Assert.Empty(Directory.EnumerateFiles(root, $"*{TrackerStorageNames.TemporaryExtension}"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies a failed final replacement removes its uniquely named temporary file.
    /// </summary>
    [Fact]
    public void FailedReplacementRemovesTemporaryFile()
    {
        string root = CreateRoot();
        string path = Path.Combine(root, "occupied.json");
        Directory.CreateDirectory(path);
        try
        {
            Exception? exception = Record.Exception(() => TrackerAtomicFileWriter.WriteAllText(path, "content"));

            Assert.True(exception is IOException or UnauthorizedAccessException);
            Assert.Empty(Directory.EnumerateFiles(root, $"*{TrackerStorageNames.TemporaryExtension}"));
            Assert.True(Directory.Exists(path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Creates an isolated persistence root.
    /// </summary>
    /// <returns>The isolated directory path.</returns>
    private static string CreateRoot()
        => Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
}
