namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Describes one ordered Ironmon fusion calculation and its result.
/// </summary>
public sealed class FusionOutcomeSnapshot
{
    /// <summary>
    /// Initializes an empty fusion outcome for protocol serialization.
    /// </summary>
    public FusionOutcomeSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the body material.
    /// </summary>
    public required PokemonRelationSnapshot Body { get; init; }

    /// <summary>
    /// Gets or initializes the head material.
    /// </summary>
    public required PokemonRelationSnapshot Head { get; init; }

    /// <summary>
    /// Gets or initializes the deterministic Ironmon result.
    /// </summary>
    public required PokemonRelationSnapshot Result { get; init; }
}
