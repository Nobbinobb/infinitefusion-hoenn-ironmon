namespace Ironmon.Tracker.Protocol.Live;

/// <summary>
/// Describes one legally visible starter-selection slot.
/// </summary>
public sealed class StarterChoiceSnapshot
{
    /// <summary>
    /// Initializes an empty starter-choice snapshot for protocol serialization.
    /// </summary>
    public StarterChoiceSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the zero-based position of the starter choice.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Gets or initializes whether the player has revealed this choice in the game.
    /// </summary>
    public bool Revealed { get; init; }

    /// <summary>
    /// Gets or initializes the stable species and form identifier after revelation.
    /// </summary>
    public string? SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the localized species name after revelation.
    /// </summary>
    public string? SpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the sprite path relative to the game root after revelation.
    /// </summary>
    public string? SpritePath { get; init; }

    /// <summary>
    /// Gets or initializes the generated base-stat total after revelation.
    /// </summary>
    public int? BaseStatTotal { get; init; }

    /// <summary>
    /// Gets or initializes whether the revealed starter is covered by the Favorite Clause.
    /// </summary>
    public bool Favorite { get; init; }
}
