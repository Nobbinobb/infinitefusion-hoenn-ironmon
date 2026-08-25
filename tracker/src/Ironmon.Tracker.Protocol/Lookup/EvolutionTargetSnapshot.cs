namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Describes one generated evolution destination shown by authorized lookup.
/// </summary>
public sealed class EvolutionTargetSnapshot
{
    /// <summary>
    /// Initializes an empty evolution target for protocol serialization.
    /// </summary>
    public EvolutionTargetSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the stable destination species identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the localized destination name.
    /// </summary>
    public required string SpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the resolved game-relative sprite path.
    /// </summary>
    public string? SpritePath { get; init; }

    /// <summary>
    /// Gets or initializes the generated destination base-stat total.
    /// </summary>
    public int BaseStatTotal { get; init; }

    /// <summary>
    /// Gets or initializes the stable graph row derived from native component stages.
    /// </summary>
    public int StageLevel { get; init; }

    /// <summary>
    /// Gets or initializes the fusion component that activates this relationship.
    /// </summary>
    public EvolutionCandidateSide ComponentSide { get; init; } = EvolutionCandidateSide.Normal;

    /// <summary>
    /// Gets or initializes every effective evolution-method label for the destination branch.
    /// </summary>
    public IReadOnlyList<string> EffectiveMethods { get; init; } = [];

    /// <summary>
    /// Gets or initializes the passive run-obtainability state when that information domain is available.
    /// </summary>
    public PokemonObtainabilityStatus? ObtainabilityStatus { get; init; }
}
