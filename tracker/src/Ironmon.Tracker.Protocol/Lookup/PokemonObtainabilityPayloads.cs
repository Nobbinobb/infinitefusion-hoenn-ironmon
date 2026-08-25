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
    /// Gets or initializes the tracker-computed obtainability closure.
    /// </summary>
    public PlayerFusionClosureResultPayload? FusionClosureResult { get; init; }

    /// <summary>
    /// Gets or initializes whether this tracker cannot reproduce the game-owned closure job.
    /// </summary>
    public bool FusionClosureWorkerUnavailable { get; init; }

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
    /// Gets or initializes the tracker-computed obtainability closure.
    /// </summary>
    public PlayerFusionClosureResultPayload? FusionClosureResult { get; init; }

    /// <summary>
    /// Gets or initializes whether this tracker cannot reproduce the game-owned closure job.
    /// </summary>
    public bool FusionClosureWorkerUnavailable { get; init; }
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
    public PokemonObtainabilityStatus Status { get; init; } = PokemonObtainabilityStatus.Calculating;

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
    /// Gets or initializes whether cooperative background work has reached its foreground-only boundary.
    /// </summary>
    public bool BackgroundComplete { get; init; }

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
    /// Gets or initializes the currently proven identifiers from the bounded set requested by the caller.
    /// </summary>
    public IReadOnlyList<string> ObtainableSpeciesIds { get; init; } = [];

    /// <summary>
    /// Gets or initializes the requested evolution-edge keys backed by a proven executable route.
    /// </summary>
    public IReadOnlyList<string> ObtainableEvolutionEdgeKeys { get; init; } = [];

    /// <summary>
    /// Gets or initializes the tracker-side fusion closure job when compatible work is available.
    /// </summary>
    public PlayerFusionClosureWorkPayload? FusionClosureWork { get; init; }

    /// <summary>
    /// Gets or initializes the requested target proof, when a target was supplied.
    /// </summary>
    public PokemonObtainabilitySnapshot? Target { get; init; }
}

/// <summary>
/// Describes the seed-specific fusion obtainability closure that the tracker calculates in parallel.
/// </summary>
public sealed class PlayerFusionClosureWorkPayload
{
    /// <summary>
    /// Gets or initializes the stable calculation identifier.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Gets the release-generated semantic acquisition/resource catalog fingerprint.
    /// </summary>
    public required string SourceCatalogFingerprint { get; init; }

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
    /// Gets or initializes the total unordered pair count.
    /// </summary>
    public int TotalPairs { get; init; }

    /// <summary>
    /// Gets or initializes the unordered pair offsets whose normal-material proof states are incompatible.
    /// </summary>
    public IReadOnlyList<int> ExcludedPairOffsets { get; init; } = [];

    /// <summary>
    /// Gets the compact normal and caught-fusion proof states that seed tracker-owned closure.
    /// </summary>
    public IReadOnlyList<PlayerFusionProofSpeciesPayload> BaseProofs { get; init; } = [];

    /// <summary>
    /// Gets the run-wide quantities available to item-consuming evolution methods.
    /// </summary>
    public IReadOnlyDictionary<string, int> ResourceSupply { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Gets the fusion-evolution generator version required by this run.
    /// </summary>
    public int FusionEvolutionGeneratorVersion { get; init; }

    /// <summary>
    /// Gets the fusion-evolution rules version required by this run.
    /// </summary>
    public int FusionEvolutionRulesVersion { get; init; }

    /// <summary>
    /// Gets the evolution source fingerprint required by this run.
    /// </summary>
    public string EvolutionSourceFingerprint { get; init; } = string.Empty;

    /// <summary>
    /// Gets the evolution taxonomy fingerprint required by this run.
    /// </summary>
    public string EvolutionTaxonomyFingerprint { get; init; } = string.Empty;

    /// <summary>
    /// Gets the evolution method fingerprint required by this run.
    /// </summary>
    public string EvolutionMethodFingerprint { get; init; } = string.Empty;

}

/// <summary>
/// Carries every nondominated base proof for one normal or directly caught fusion species.
/// </summary>
public sealed class PlayerFusionProofSpeciesPayload
{
    /// <summary>
    /// Gets the numeric species identifier.
    /// </summary>
    public int SpeciesId { get; init; }

    /// <summary>
    /// Gets the bounded nondominated proof states for the species.
    /// </summary>
    public IReadOnlyList<PlayerFusionProofPlanPayload> Plans { get; init; } = [];
}

/// <summary>
/// Carries the feasibility state of one acquisition proof without its display path.
/// </summary>
public sealed class PlayerFusionProofPlanPayload
{
    /// <summary>
    /// Gets the evolution-item quantities consumed by the proof.
    /// </summary>
    public IReadOnlyDictionary<string, int> Items { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Gets mutually exclusive run choices made by the proof.
    /// </summary>
    public IReadOnlyDictionary<string, string> Constraints { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets unique acquisition-source quantities consumed by the proof.
    /// </summary>
    public IReadOnlyDictionary<string, int> SourceUses { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Gets the number of acquisition operations in the proof.
    /// </summary>
    public int PathLength { get; init; }
}

/// <summary>
/// Returns a tracker-computed player-fusion result to the game.
/// </summary>
public sealed class PlayerFusionClosureResultPayload
{
    /// <summary>
    /// Gets or initializes the calculation identifier supplied by the game.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Gets the packed final obtainability bitset in numeric custom-fusion order.
    /// </summary>
    public IReadOnlyList<uint> ObtainableFusionWords { get; init; } = [];

    /// <summary>
    /// Gets sorted executable evolution edges packed as unsigned delta varints.
    /// </summary>
    public byte[] PackedExecutableEvolutionEdges { get; init; } = [];

    /// <summary>
    /// Gets the final number of normal and fusion species with an acquisition proof.
    /// </summary>
    public int ObtainableCount { get; init; }

}
