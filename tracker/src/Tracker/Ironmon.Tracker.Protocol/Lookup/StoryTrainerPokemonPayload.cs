namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Preserves one actual story-trainer party member independently of placeholder PBS teams.
/// </summary>
public sealed class StoryTrainerPokemonPayload
{
    /// <summary>
    /// Initializes an empty party member for completed-run serialization.
    /// </summary>
    public StoryTrainerPokemonPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the generated species and form identity at battle admission.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the effective battle level.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Gets or initializes the selected battle ability identifier.
    /// </summary>
    public required string AbilityId { get; init; }

    /// <summary>
    /// Gets or initializes the equipped move identifiers in battle-slot order.
    /// </summary>
    public IReadOnlyList<string> MoveIds { get; init; } = [];
}
