namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Describes tracker-owned gameplay assistance settings shared with the game.
/// </summary>
public sealed class TrackerSettingsPayload
{
    /// <summary>
    /// Gets or initializes the independent feature switches, or null to retain the current switches.
    /// </summary>
    public TrackerFeatureSettings? Features { get; init; }

    /// <summary>
    /// Gets or initializes a complete toggle profile, or null to retain the current profile.
    /// </summary>
    public ToggleSpeedConfiguration? ToggleSpeed { get; init; }

    /// <summary>
    /// Gets or sets the live automatic-reset preference.
    /// </summary>
    public bool AutomaticReset { get; set; } = true;

    /// <summary>
    /// Initializes an empty tracker-settings payload for protocol serialization.
    /// </summary>
    public TrackerSettingsPayload()
    {
    }

    /// <summary>
    /// Gets or initializes whether starter selection is controlled automatically.
    /// </summary>
    public bool AutoSelectStarter { get; init; } = true;

    /// <summary>
    /// Gets or initializes the inclusive generated-BST ceiling for automatic starter selection.
    /// </summary>
    public int? MaximumStarterBaseStatTotal { get; init; }

    /// <summary>
    /// Gets or initializes the stable normal-species identifiers covered by the Favorite Clause.
    /// </summary>
    public IReadOnlyList<string> FavoriteSpeciesIds { get; init; } = [];
}
