namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Describes one directed generated relationship rendered in an evolution graph neighborhood.
/// </summary>
public sealed class EvolutionGraphEdge
{
    /// <summary>
    /// Initializes one graph relationship.
    /// </summary>
    /// <param name="sourceSpeciesId">The source species identifier.</param>
    /// <param name="targetSpeciesId">The destination species identifier.</param>
    /// <param name="effectiveMethods">The effective relationship requirements.</param>
    /// <param name="componentSide">The fusion component that activates the relationship.</param>
    public EvolutionGraphEdge(string sourceSpeciesId, string targetSpeciesId, IReadOnlyList<string> effectiveMethods, EvolutionCandidateSide componentSide)
    {
        SourceSpeciesId = sourceSpeciesId;
        TargetSpeciesId = targetSpeciesId;
        EffectiveMethods = effectiveMethods;
        ComponentSide = componentSide;
    }

    /// <summary>
    /// Gets the source species identifier.
    /// </summary>
    public string SourceSpeciesId { get; }

    /// <summary>
    /// Gets the destination species identifier.
    /// </summary>
    public string TargetSpeciesId { get; }

    /// <summary>
    /// Gets the effective relationship requirements.
    /// </summary>
    public IReadOnlyList<string> EffectiveMethods { get; }

    /// <summary>
    /// Gets the fusion component that activates the relationship.
    /// </summary>
    public EvolutionCandidateSide ComponentSide { get; }

    /// <summary>
    /// Gets the stable key used to deduplicate this directed relationship.
    /// </summary>
    public string Key => $"{SourceSpeciesId}>{TargetSpeciesId}";
}
