namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Carries generated-evolution outcomes observed during one completed run.
/// </summary>
public sealed class EvolutionMetricsPayload
{
    /// <summary>
    /// Gets or initializes the metrics schema version.
    /// </summary>
    public int SchemaVersion { get; init; }

    /// <summary>
    /// Gets or initializes the observed evolution events.
    /// </summary>
    public IReadOnlyList<EvolutionMetricPayload> Events { get; init; } = [];
}

/// <summary>
/// Describes one offered, completed, or cancelled generated evolution.
/// </summary>
public sealed class EvolutionMetricPayload
{
    /// <summary>
    /// Gets or initializes the run-local event identifier.
    /// </summary>
    public int EventId { get; init; }

    /// <summary>
    /// Gets or initializes the Pokemon personal identifier.
    /// </summary>
    public required string PokemonId { get; init; }

    /// <summary>
    /// Gets or initializes whether the source was normal or a fusion.
    /// </summary>
    public required string SourceKind { get; init; }

    /// <summary>
    /// Gets or initializes the source species identifier.
    /// </summary>
    public required string SourceSpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the displayed source species name.
    /// </summary>
    public required string SourceSpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the selected target species identifier.
    /// </summary>
    public required string TargetSpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the displayed target species name.
    /// </summary>
    public required string TargetSpeciesName { get; init; }

    /// <summary>
    /// Gets or initializes the Pokemon level when evolution was offered.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Gets or initializes the activation context.
    /// </summary>
    public required string ActivationContext { get; init; }

    /// <summary>
    /// Gets or initializes the effective evolution method.
    /// </summary>
    public string? EffectiveMethod { get; init; }

    /// <summary>
    /// Gets or initializes the serialized effective method parameter.
    /// </summary>
    public string? EffectiveParameter { get; init; }

    /// <summary>
    /// Gets or initializes the evolving fusion side when applicable.
    /// </summary>
    public string? ComponentSide { get; init; }

    /// <summary>
    /// Gets or initializes whether the evolution was forced.
    /// </summary>
    public bool Forced { get; init; }

    /// <summary>
    /// Gets or initializes whether closest-BST fallback selected the target.
    /// </summary>
    public bool Fallback { get; init; }

    /// <summary>
    /// Gets or initializes the source generated BST.
    /// </summary>
    public int SourceBst { get; init; }

    /// <summary>
    /// Gets or initializes the natural reference generated BST.
    /// </summary>
    public int ReferenceBst { get; init; }

    /// <summary>
    /// Gets or initializes the selected target generated BST.
    /// </summary>
    public int TargetBst { get; init; }

    /// <summary>
    /// Gets or initializes the offered, completed, or cancelled outcome.
    /// </summary>
    public required string Outcome { get; init; }

    /// <summary>
    /// Gets or initializes the duplicate target species identifier when produced.
    /// </summary>
    public string? DuplicateTargetSpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the displayed duplicate target species name when produced.
    /// </summary>
    public string? DuplicateTargetSpeciesName { get; init; }
}
