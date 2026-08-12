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
    /// <exception cref="ArgumentException">Thrown when the tracker version is empty.</exception>
    /// <param name="favoriteSpeciesIds">The normal species covered by the Favorite Clause.</param>
    public TrackerHandshakePayload(string trackerVersion, bool debugRequested, bool autoSelectStarter = false, IReadOnlyList<string>? favoriteSpeciesIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackerVersion);
        TrackerVersion = trackerVersion;
        DebugRequested = debugRequested;
        AutoSelectStarter = autoSelectStarter;
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
    /// Gets the stable normal-species identifiers covered by the Favorite Clause.
    /// </summary>
    public IReadOnlyList<string> FavoriteSpeciesIds { get; }
}
