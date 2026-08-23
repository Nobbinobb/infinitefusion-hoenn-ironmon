namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies replacement, keyed independence, reset, and disposal for latest-request coordination.
/// </summary>
public sealed class LatestRequestCoordinatorTests
{
    /// <summary>
    /// Verifies that a newer request cancels and supersedes the previous request using the same key.
    /// </summary>
    [Fact]
    public void BeginReplacesRequestUsingSameKey()
    {
        using LatestRequestCoordinator<string> coordinator = new();
        using LatestRequestLease<string> first = coordinator.Begin("lookup");
        using LatestRequestLease<string> second = coordinator.Begin("lookup");

        Assert.True(first.CancellationToken.IsCancellationRequested);
        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
    }

    /// <summary>
    /// Verifies that synchronous cancellation callbacks may inspect coordinator state without deadlocking.
    /// </summary>
    [Fact]
    public void ReplacementCallbackCanInspectCoordinatorState()
    {
        using LatestRequestCoordinator<string> coordinator = new();
        using LatestRequestLease<string> first = coordinator.Begin("lookup");
        bool? wasCurrentDuringCancellation = null;
        using CancellationTokenRegistration registration = first.CancellationToken.Register(() => wasCurrentDuringCancellation = first.IsCurrent);

        using LatestRequestLease<string> second = coordinator.Begin("lookup");

        Assert.False(wasCurrentDuringCancellation);
        Assert.True(second.IsCurrent);
    }

    /// <summary>
    /// Verifies that separate keys can remain current concurrently.
    /// </summary>
    [Fact]
    public void SeparateKeysRemainIndependent()
    {
        using LatestRequestCoordinator<string> coordinator = new();
        using LatestRequestLease<string> summary = coordinator.Begin("summary");
        using LatestRequestLease<string> details = coordinator.Begin("details");

        Assert.True(summary.IsCurrent);
        Assert.True(details.IsCurrent);
        Assert.True(summary.Complete());
        Assert.True(details.IsCurrent);
    }

    /// <summary>
    /// Verifies that completing an older lease cannot remove its replacement.
    /// </summary>
    [Fact]
    public void SupersededCompletionPreservesReplacement()
    {
        using LatestRequestCoordinator<string> coordinator = new();
        using LatestRequestLease<string> first = coordinator.Begin("lookup");
        using LatestRequestLease<string> second = coordinator.Begin("lookup");

        Assert.False(first.Complete());
        Assert.True(second.IsCurrent);
        Assert.True(second.Complete());
    }

    /// <summary>
    /// Verifies that a reset cancels every keyed request without disposing the coordinator.
    /// </summary>
    [Fact]
    public void CancelAllAllowsFreshRequests()
    {
        using LatestRequestCoordinator<string> coordinator = new();
        using LatestRequestLease<string> first = coordinator.Begin("first");
        using LatestRequestLease<string> second = coordinator.Begin("second");

        coordinator.CancelAll();

        Assert.True(first.CancellationToken.IsCancellationRequested);
        Assert.True(second.CancellationToken.IsCancellationRequested);
        Assert.False(first.IsCurrent);
        Assert.False(second.IsCurrent);
        using LatestRequestLease<string> replacement = coordinator.Begin("first");
        Assert.True(replacement.IsCurrent);
    }

    /// <summary>
    /// Verifies that disposal cancels active requests and rejects new work.
    /// </summary>
    [Fact]
    public void DisposeCancelsAndRejectsRequests()
    {
        LatestRequestCoordinator<string> coordinator = new();
        using LatestRequestLease<string> request = coordinator.Begin("lookup");

        coordinator.Dispose();

        Assert.True(request.CancellationToken.IsCancellationRequested);
        Assert.False(request.IsCurrent);
        Assert.Throws<ObjectDisposedException>(() => coordinator.Begin("replacement"));
    }
}
