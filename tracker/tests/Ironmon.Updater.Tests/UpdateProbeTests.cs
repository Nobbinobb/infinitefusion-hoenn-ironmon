using Ironmon.Updater.Core;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Verifies that the pure core can be driven by deterministic feed and clock fixtures.
/// </summary>
public sealed class UpdateProbeTests
{
    private const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>
    /// Returns the fake provider's exact target with the controlled observation time.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task UsesInjectedReleaseAndClock()
    {
        var clock = new FixedClock();
        var release = new UpdateRelease(new Version(0, 9, 0), Commit);
        var probe = new UpdateProbe(new FakeReleases(release), clock);
        var first = await probe.CheckAsync();
        clock.Advance(TimeSpan.FromHours(1));
        var second = await probe.CheckAsync();
        Assert.Same(release, first.Release);
        Assert.Equal(TimeSpan.FromHours(1), second.CheckedAt - first.CheckedAt);
    }

    /// <summary>
    /// Propagates cancellation instead of reporting a completed check.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CancellationDoesNotReturnAnObservation()
    {
        var probe = new UpdateProbe(new FakeReleases(null), new FixedClock());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.CheckAsync(new CancellationToken(true)));
    }

    /// <summary>
    /// Supplies a deterministic release without HTTP.
    /// </summary>
    /// <remarks>
    /// Initializes the fake feed.
    /// </remarks>
    /// <param name="release">The supplied release.</param>
    private sealed class FakeReleases(UpdateRelease? release) : IUpdateReleaseProvider
    {
        /// <summary>
        /// Returns the configured release without observing cancellation, exercising the probe's own cancellation check.
        /// </summary>
        /// <param name="cancellationToken">The token deliberately ignored by this fixture.</param>
        /// <returns>A completed task containing the configured release, which may be null.</returns>
        public Task<UpdateRelease?> GetLatestAsync(CancellationToken cancellationToken) => Task.FromResult(release);
    }

    /// <summary>
    /// Controls observation times without wall-clock waiting.
    /// </summary>
    private sealed class FixedClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        /// <summary>
        /// Gets the controlled fixture time, initially the Unix epoch.
        /// </summary>
        /// <returns>The current fixture time without advancing it.</returns>
        public override DateTimeOffset GetUtcNow()
            => _now;

        /// <summary>
        /// Advances fixture time.
        /// </summary>
        /// <param name="elapsed">The duration to add to the controlled time.</param>
        internal void Advance(TimeSpan elapsed)
            => _now += elapsed;
    }
}
