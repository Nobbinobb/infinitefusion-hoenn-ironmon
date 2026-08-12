namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Describes tracker-owned gameplay assistance settings shared with the game.
/// </summary>
public sealed class TrackerSettingsPayload
{
    /// <summary>
    /// Initializes an empty tracker-settings payload for protocol serialization.
    /// </summary>
    public TrackerSettingsPayload()
    {
    }

    /// <summary>
    /// Gets or initializes whether starter selection is controlled automatically.
    /// </summary>
    public bool AutoSelectStarter { get; init; }
}
