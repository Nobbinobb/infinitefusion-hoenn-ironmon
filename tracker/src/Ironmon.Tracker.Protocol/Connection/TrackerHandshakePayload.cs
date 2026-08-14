namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Describes the tracker application during the initial handshake.
/// </summary>
public sealed class TrackerHandshakePayload
{
    /// <summary>
    /// Initializes a tracker handshake payload.
    /// </summary>
    /// <param name="trackerVersion">The tracker application version.</param>
    /// <param name="debugRequested">Whether the tracker was launched in debug mode.</param>
    /// <param name="autoSelectStarter">Whether starter selection is controlled automatically.</param>
    /// <param name="maximumStarterBaseStatTotal">The inclusive generated-BST ceiling for automatic starter selection.</param>
    /// <param name="favoriteSpeciesIds">The normal species covered by the Favorite Clause.</param>
    /// <exception cref="ArgumentException">Thrown when the tracker version is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the maximum starter BST is outside the supported range.</exception>
    public TrackerHandshakePayload(string trackerVersion, bool debugRequested, bool autoSelectStarter = false, int? maximumStarterBaseStatTotal = null, IReadOnlyList<string>? favoriteSpeciesIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackerVersion);
        if (maximumStarterBaseStatTotal is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumStarterBaseStatTotal.Value, StarterSelectionConstants.MinimumBaseStatTotal);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumStarterBaseStatTotal.Value, StarterSelectionConstants.MaximumBaseStatTotal);
        }

        TrackerVersion = trackerVersion;
        DebugRequested = debugRequested;
        AutoSelectStarter = autoSelectStarter;
        MaximumStarterBaseStatTotal = maximumStarterBaseStatTotal;
        FavoriteSpeciesIds = favoriteSpeciesIds ?? [];
    }

    /// <summary>
    /// Gets the tracker application version.
    /// </summary>
    public string TrackerVersion { get; }

    /// <summary>
    /// Gets whether the tracker was launched in debug mode.
    /// </summary>
    public bool DebugRequested { get; }

    /// <summary>
    /// Gets whether starter selection is controlled automatically.
    /// </summary>
    public bool AutoSelectStarter { get; }

    /// <summary>
    /// Gets the inclusive generated-BST ceiling for automatic starter selection.
    /// </summary>
    public int? MaximumStarterBaseStatTotal { get; }

    /// <summary>
    /// Gets the stable normal-species identifiers covered by the Favorite Clause.
    /// </summary>
    public IReadOnlyList<string> FavoriteSpeciesIds { get; }
}
