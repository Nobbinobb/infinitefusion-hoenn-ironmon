namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Describes one ordered pair of normal Pokemon that produces an Ironmon fusion.
/// </summary>
public sealed class FusionMaterialPairSnapshot
{
    /// <summary>
    /// Initializes an empty fusion material pair for protocol serialization.
    /// </summary>
    public FusionMaterialPairSnapshot()
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
}
