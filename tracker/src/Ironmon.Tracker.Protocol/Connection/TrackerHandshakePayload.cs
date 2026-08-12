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
    public TrackerHandshakePayload(string trackerVersion, bool debugRequested, bool autoSelectStarter = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackerVersion);
        TrackerVersion = trackerVersion;
        DebugRequested = debugRequested;
        AutoSelectStarter = autoSelectStarter;
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
}
