using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises actual Windows sharing locks without weakening journal durability or authenticated tree checks.
/// </summary>
public sealed class TransactionContentionTests
{
    private const string _document = "journal.bin";
    private const string _source = "source";
    private const string _destination = "destination";
    private const string _file = "index";
    private const int _releaseDelayMilliseconds = 150;

    /// <summary>
    /// Waits for a short ordinary-reader lock while preserving the complete previous durable generation.
    /// </summary>
    /// <returns>Completion after the lock owner and atomic replacement finish.</returns>
    [Fact]
    public async Task DurableReplacementWaitsForTransientReader()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.PathFor(_document);
        TransactionStorage.WriteDurable(path, [1]);
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = ReleaseLaterAsync(reader);
        TransactionStorage.WriteDurable(path, [2]);
        await release;
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(path));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(path + TransactionStorage.PreviousSuffix));
    }

    /// <summary>
    /// Keeps the original durable bytes when the sharing lock does not clear within the bounded wait.
    /// </summary>
    [Fact]
    public void PermanentReaderCannotReportSuccessfulReplacement()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.PathFor(_document);
        TransactionStorage.WriteDurable(path, [1]);
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var error = Record.Exception(() => TransactionStorage.WriteDurable(path, [2]));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(path));
    }

    /// <summary>
    /// Promotes the verified tree after a native parent-directory lease has been released.
    /// </summary>
    /// <returns>Completion after the ordinary same-volume directory move.</returns>
    [Fact]
    public async Task TreePromotionWaitsForTransientDirectoryLock()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native directory sharing-lock validation requires Windows.");

        using var workspace = new TestWorkspace();
        var source = workspace.PathFor(_source);
        var destination = workspace.PathFor(_destination);
        TransactionFixture.Write(source, _file, 1);
        var expected = await TransactionStorage.TreeAsync(source, CancellationToken.None);
        using var held = new FileStream(Path.Combine(source, _file), FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = ReleaseLaterAsync(held);
        await TransactionStorage.MoveTreeAsync(source, destination, expected, CancellationToken.None);
        await release;
        Assert.False(Directory.Exists(source));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(destination, _file)));
    }

    /// <summary>
    /// Refuses changed contents observed during a contention retry and preserves the unexpected bytes for review.
    /// </summary>
    /// <returns>Completion after the retry rejects the altered snapshot.</returns>
    [Fact]
    public async Task ContendedTreeMustStillMatchItsAuthenticatedSnapshot()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native directory sharing-lock validation requires Windows.");

        using var workspace = new TestWorkspace();
        var source = workspace.PathFor(_source);
        var destination = workspace.PathFor(_destination);
        TransactionFixture.Write(source, _file, 1);
        var expected = await TransactionStorage.TreeAsync(source, CancellationToken.None);
        TransactionFixture.Write(source, _file, 9);
        using var held = new FileStream(Path.Combine(source, _file), FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = ReleaseLaterAsync(held);
        await Assert.ThrowsAsync<InvalidDataException>(() => TransactionStorage.MoveTreeAsync(source, destination, expected, CancellationToken.None));
        await release;
        Assert.False(Directory.Exists(destination));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(source, _file)));
    }

    /// <summary>
    /// Releases only the lock owned by this fixture after a bounded interval.
    /// </summary>
    /// <param name="held">The exact ordinary file or directory lease owned by the test.</param>
    /// <returns>Completion after releasing the lock.</returns>
    private static async Task ReleaseLaterAsync(IDisposable held)
    {
        await Task.Delay(_releaseDelayMilliseconds).ConfigureAwait(false);
        held.Dispose();
    }
}
