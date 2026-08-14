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

    /// <summary>
    /// Gets or initializes the inclusive generated-BST ceiling for automatic starter selection.
    /// </summary>
    public int? MaximumStarterBaseStatTotal { get; init; }

    /// <summary>
    /// Gets or initializes the stable normal-species identifiers covered by the Favorite Clause.
    /// </summary>
    public IReadOnlyList<string> FavoriteSpeciesIds { get; init; } = [];
}
