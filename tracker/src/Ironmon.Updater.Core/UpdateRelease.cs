namespace Ironmon.Updater.Core;

/// <summary>
/// Describes an approved target without binding the core to a feed or user interface.
/// </summary>
/// <remarks>
/// Initializes a target supplied by a trusted release provider; this record does not authenticate it.
/// </remarks>
/// <param name="Version">The Ironmon version.</param>
/// <param name="GameCommit">The exact approved game commit.</param>
public sealed record UpdateRelease(Version Version, string GameCommit);

/// <summary>
/// Supplies authenticated releases independently of tracker startup and presentation.
/// </summary>
public interface IUpdateReleaseProvider
{
    /// <summary>
    /// Reads the latest eligible release, or null when no release is available.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task whose result is the latest eligible release, or <see langword="null" /> when none is available.</returns>
    Task<UpdateRelease?> GetLatestAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Captures a deterministic availability observation.
/// </summary>
/// <remarks>
/// Initializes the result of a completed availability check.
/// </remarks>
/// <param name="Release">The observed target, or <see langword="null" /> when none is available.</param>
/// <param name="CheckedAt">The clock time of the completed observation.</param>
public sealed record UpdateObservation(UpdateRelease? Release, DateTimeOffset CheckedAt);

/// <summary>
/// Provides a UI-independent seam for release and clock fixtures.
/// </summary>
/// <remarks>
/// Initializes the probe with its feed and clock.
/// </remarks>
/// <param name="releases">The release provider.</param>
/// <param name="clock">The injected clock.</param>
public sealed class UpdateProbe(IUpdateReleaseProvider releases, TimeProvider clock)
{
    /// <summary>
    /// Observes a release without downloading or changing an installation.
    /// </summary>
    /// <remarks>
    /// Rechecks cancellation after the provider returns, including when the provider ignores the token.
    /// </remarks>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task whose result contains the release and the clock time sampled after the provider completes.</returns>
    public async Task<UpdateObservation> CheckAsync(CancellationToken cancellationToken = default)
    {
        var release = await releases.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new UpdateObservation(release, clock.GetUtcNow());
    }
}
