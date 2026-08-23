namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Requests a snapshot of a completed run's shared obtainability calculation.
/// </summary>
public sealed class PokemonObtainabilityRequestPayload
{
    /// <summary>
    /// Initializes an empty obtainability request for protocol serialization.
    /// </summary>
    public PokemonObtainabilityRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the optional target whose witness should be returned.
    /// </summary>
    public string? SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the bounded set of species identifiers whose proven membership should be returned.
    /// </summary>
    public IReadOnlyList<string> SpeciesIds { get; init; } = [];

    /// <summary>
    /// Gets or initializes the bounded evolution-edge keys whose possible membership should be returned.
    /// </summary>
    public IReadOnlyList<string> EvolutionEdgeKeys { get; init; } = [];

    /// <summary>
    /// Gets or initializes whether opening a calculation-dependent view should temporarily prioritize this run.
    /// </summary>
    public bool Foreground { get; init; }

    /// <summary>
    /// Gets or initializes one tracker-computed material-mapping batch.
    /// </summary>
    public PlayerFusionMappingBatchPayload? FusionMappingBatch { get; init; }

    /// <summary>
    /// Gets or initializes whether this tracker cannot reproduce the game-owned mapping job.
    /// </summary>
    public bool FusionMappingWorkerUnavailable { get; init; }

    /// <summary>
    /// Gets or initializes the completed-run reconstruction recipe.
    /// </summary>
    public required CompletedRunRecipePayload Recipe { get; init; }
}

/// <summary>
/// Requests a snapshot of the authorized active run's shared obtainability calculation.
/// </summary>
public sealed class DebugPokemonObtainabilityRequestPayload
{
    /// <summary>
    /// Initializes an empty active-run obtainability request for protocol serialization.
    /// </summary>
    public DebugPokemonObtainabilityRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the optional target whose witness should be returned.
    /// </summary>
    public string? SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the bounded set of species identifiers whose proven membership should be returned.
    /// </summary>
    public IReadOnlyList<string> SpeciesIds { get; init; } = [];

    /// <summary>
    /// Gets or initializes the bounded evolution-edge keys whose possible membership should be returned.
    /// </summary>
    public IReadOnlyList<string> EvolutionEdgeKeys { get; init; } = [];

    /// <summary>
    /// Gets or initializes whether opening a calculation-dependent view should temporarily prioritize this run.
    /// </summary>
    public bool Foreground { get; init; }

    /// <summary>
    /// Gets or initializes one tracker-computed material-mapping batch.
    /// </summary>
    public PlayerFusionMappingBatchPayload? FusionMappingBatch { get; init; }

    /// <summary>
    /// Gets or initializes whether this tracker cannot reproduce the game-owned mapping job.
    /// </summary>
    public bool FusionMappingWorkerUnavailable { get; init; }
}

/// <summary>
/// Describes one run-specific obtainability proof or current proof state.
/// </summary>
public sealed class PokemonObtainabilitySnapshot
{
    /// <summary>
    /// Initializes an empty obtainability snapshot for protocol serialization.
    /// </summary>
    public PokemonObtainabilitySnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the current proof status.
    /// </summary>
    public PokemonObtainabilityStatus Status { get; init; } = PokemonObtainabilityStatus.Unknown;

    /// <summary>
    /// Gets or initializes the concise explanation for the status.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes one valid acquisition witness path when proven.
    /// </summary>
    public IReadOnlyList<string> Path { get; init; } = [];

    /// <summary>
    /// Gets or initializes the evolution-item quantities consumed by the witness path.
    /// </summary>
    public IReadOnlyDictionary<string, int> RequiredItems { get; init; } = new Dictionary<string, int>();
}

/// <summary>
/// Reports progress and the currently proven set from one shared obtainability calculation.
/// </summary>
public sealed class PokemonObtainabilityResponsePayload
{
    /// <summary>
    /// Initializes an empty obtainability response for protocol serialization.
    /// </summary>
    public PokemonObtainabilityResponsePayload()
    {
    }

    /// <summary>
    /// Gets or initializes the current calculation phase.
    /// </summary>
    public string Phase { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes whether every material pair and evolution closure has been processed.
    /// </summary>
    public bool Complete { get; init; }

    /// <summary>
    /// Gets or initializes the number of processed unordered material pairs.
    /// </summary>
    public int ProcessedPairs { get; init; }

    /// <summary>
    /// Gets or initializes the total number of unordered material pairs.
    /// </summary>
    public int TotalPairs { get; init; }

    /// <summary>
    /// Gets or initializes the current number of species with a proven path.
    /// </summary>
    public int ObtainableCount { get; init; }

    /// <summary>
    /// Gets or initializes the number of authored acquisition calls that need a future semantic adapter.
    /// </summary>
    public int UnresolvedSourceCount { get; init; }

    /// <summary>
    /// Gets or initializes the number of resource-source categories that still need path semantics.
    /// </summary>
    public int UnresolvedResourceCount { get; init; }

    /// <summary>
    /// Gets or initializes the active material-mapping implementation.
    /// </summary>
    public string FusionMappingMode { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the number of tracker-computed batches accepted by the game.
    /// </summary>
    public int TrackerMappingBatchesApplied { get; init; }

    /// <summary>
    /// Gets or initializes the number of material pairs mapped by the Ruby fallback.
    /// </summary>
    public int RubyMappingPairsProcessed { get; init; }

    /// <summary>
    /// Gets or initializes the currently proven identifiers from the bounded set requested by the caller.
    /// </summary>
    public IReadOnlyList<string> ObtainableSpeciesIds { get; init; } = [];

    /// <summary>
    /// Gets or initializes the requested evolution-edge keys backed by a proven executable route.
    /// </summary>
    public IReadOnlyList<string> ObtainableEvolutionEdgeKeys { get; init; } = [];

    /// <summary>
    /// Gets or initializes the remaining tracker-side material-mapping job when compatible work is available.
    /// </summary>
    public PlayerFusionMappingWorkPayload? FusionMappingWork { get; init; }

    /// <summary>
    /// Gets or initializes the requested target proof, when a target was supplied.
    /// </summary>
    public PokemonObtainabilitySnapshot? Target { get; init; }
}

/// <summary>
/// Describes the seed-specific material mapping that the tracker can calculate in parallel.
/// </summary>
public sealed class PlayerFusionMappingWorkPayload
{
    /// <summary>
    /// Gets or initializes the stable calculation identifier.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Gets or initializes the run seed.
    /// </summary>
    public long Seed { get; init; }

    /// <summary>
    /// Gets or initializes the player-fusion generator version.
    /// </summary>
    public int GeneratorVersion { get; init; }

    /// <summary>
    /// Gets or initializes the base-stat source fingerprint.
    /// </summary>
    public required string BaseStatSourceFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the custom-fusion pool schema version.
    /// </summary>
    public int CustomFusionPoolVersion { get; init; }

    /// <summary>
    /// Gets or initializes the custom-fusion pool size.
    /// </summary>
    public int CustomFusionPoolSize { get; init; }

    /// <summary>
    /// Gets or initializes the custom-fusion pool fingerprint.
    /// </summary>
    public required string CustomFusionPoolFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the ordered normal material identifiers.
    /// </summary>
    public IReadOnlyList<int> MaterialIds { get; init; } = [];

    /// <summary>
    /// Gets or initializes the first unordered pair not yet applied by the game.
    /// </summary>
    public int ProcessedPairs { get; init; }

    /// <summary>
    /// Gets or initializes the total unordered pair count.
    /// </summary>
    public int TotalPairs { get; init; }
}

/// <summary>
/// Returns one ordered batch of tracker-computed material mappings to the game.
/// </summary>
public sealed class PlayerFusionMappingBatchPayload
{
    /// <summary>
    /// Gets or initializes the calculation identifier supplied by the game.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based unordered-pair offset.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets or initializes the ordered mapped pairs as consecutive first-material, second-material, first-result, and second-result identifiers.
    /// </summary>
    public IReadOnlyList<int> PackedPairs { get; init; } = [];
}
