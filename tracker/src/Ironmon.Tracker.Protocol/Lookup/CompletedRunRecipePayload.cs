namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Carries the versioned deterministic inputs needed to reconstruct one completed run.
/// </summary>
public sealed class CompletedRunRecipePayload : RunReproductionRecipePayload
{
    /// <summary>
    /// Initializes an empty completed-run recipe for protocol serialization.
    /// </summary>
    public CompletedRunRecipePayload()
    {
    }

    /// <summary>
    /// Gets or initializes the completed-run recipe schema version.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// Gets or initializes the stable run identifier.
    /// </summary>
    public required string RunId { get; init; }

    /// <summary>
    /// Gets or initializes the run result identifier.
    /// </summary>
    public required string Result { get; init; }

    /// <summary>
    /// Gets or initializes the archived ordinary-item shuffle by source item identifier.
    /// </summary>
    public IReadOnlyDictionary<string, string> ItemMappings { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets or initializes the archived machine-item shuffle by source item identifier.
    /// </summary>
    public IReadOnlyDictionary<string, string> TmMappings { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets or initializes authoritative attempt statistics when the recipe provides them.
    /// </summary>
    public RunStatisticsPayload? Statistics { get; init; }
}

/// <summary>
/// Carries a completed-run recipe and whether it should become the foreground Archive selection.
/// </summary>
public sealed class RunCompletedEventPayload
{
    /// <summary>
    /// Gets or initializes the completed-run recipe to persist.
    /// </summary>
    public required CompletedRunRecipePayload Recipe { get; init; }

    /// <summary>
    /// Gets or initializes whether the tracker should open and prepare this archived run.
    /// </summary>
    public bool RequestArchiveSelection { get; init; } = true;
}

/// <summary>
/// Describes deterministic item-slot pools, bans, and shop policy.
/// </summary>
public sealed class ItemGeneratorRecipePayload
{
    /// <summary>
    /// Gets or initializes the item generator schema version.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Gets or initializes the item pool rules version.
    /// </summary>
    public int RulesVersion { get; init; }

    /// <summary>
    /// Gets or initializes the unified ground item pool size.
    /// </summary>
    public int GroundPoolSize { get; init; }

    /// <summary>
    /// Gets or initializes the total ground-selection ticket weight.
    /// </summary>
    public int GroundTotalWeight { get; init; }

    /// <summary>
    /// Gets or initializes the unified ground item pool fingerprint.
    /// </summary>
    public required string GroundPoolFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the TM gift pool size.
    /// </summary>
    public int TmPoolSize { get; init; }

    /// <summary>
    /// Gets or initializes the TM gift pool fingerprint.
    /// </summary>
    public required string TmPoolFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the ordered result-ban identifiers.
    /// </summary>
    public IReadOnlyList<string> ResultBans { get; init; } = [];

    /// <summary>
    /// Gets or initializes the result-ban fingerprint.
    /// </summary>
    public required string ResultBanFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the standard Poké Mart filtering policy version.
    /// </summary>
    public int ShopPolicyVersion { get; init; }
}

/// <summary>
/// Describes the versioned Ironmon run configuration stored in a completed recipe.
/// </summary>
public sealed class RunConfigurationPayload
{
    /// <summary>
    /// Gets or initializes the configuration schema version.
    /// </summary>
    public int SchemaVersion { get; init; }

    /// <summary>
    /// Gets or initializes the wild fusion policy.
    /// </summary>
    public required string WildPolicy { get; init; }

    /// <summary>
    /// Gets or initializes the trainer fusion policy.
    /// </summary>
    public required string TrainerPolicy { get; init; }

    /// <summary>
    /// Gets or initializes the caught-fusion unfusion policy.
    /// </summary>
    public required string UnfusionSetting { get; init; }

    /// <summary>
    /// Gets or initializes whether a completed attempt resets automatically.
    /// </summary>
    public bool AutomaticReset { get; init; }
}

/// <summary>
/// Describes species-generator compatibility metadata.
/// </summary>
public sealed class SpeciesGeneratorRecipePayload
{
    /// <summary>
    /// Gets or initializes the generator schema version.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Gets or initializes the normal-species pool fingerprint.
    /// </summary>
    public required string PoolFingerprint { get; init; }
}

/// <summary>
/// Describes ability-generator compatibility metadata.
/// </summary>
public sealed class AbilityGeneratorRecipePayload
{
    /// <summary>
    /// Gets or initializes the generator schema version.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Gets or initializes the ability-pool size.
    /// </summary>
    public int PoolSize { get; init; }

    /// <summary>
    /// Gets or initializes the ability-pool fingerprint.
    /// </summary>
    public required string PoolFingerprint { get; init; }
}

/// <summary>
/// Describes base-stat-generator compatibility metadata.
/// </summary>
public sealed class BaseStatGeneratorRecipePayload
{
    /// <summary>
    /// Gets or initializes the generator schema version.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Gets or initializes the source-stat fingerprint.
    /// </summary>
    public required string SourceFingerprint { get; init; }
}

/// <summary>
/// Describes normal and complete-fusion evolution-generator compatibility metadata.
/// </summary>
public sealed class EvolutionGeneratorRecipePayload
{
    /// <summary>
    /// Gets or initializes the normal generator schema version.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Gets or initializes the normal evolution rules version.
    /// </summary>
    public int RulesVersion { get; init; }

    /// <summary>
    /// Gets or initializes the evolution source-catalog fingerprint.
    /// </summary>
    public required string SourceFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the native evolution taxonomy fingerprint.
    /// </summary>
    public required string TaxonomyFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the effective evolution-method fingerprint.
    /// </summary>
    public required string MethodFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the normal evolution target-pool fingerprint.
    /// </summary>
    public required string TargetFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the base-stat dependency used by evolution generation.
    /// </summary>
    public required BaseStatGeneratorRecipePayload BaseStatGenerator { get; init; }

    /// <summary>
    /// Gets or initializes complete-fusion evolution metadata.
    /// </summary>
    public required FusionEvolutionGeneratorRecipePayload Fusion { get; init; }
}

/// <summary>
/// Describes complete-fusion evolution-generator compatibility metadata.
/// </summary>
public sealed class FusionEvolutionGeneratorRecipePayload
{
    /// <summary>
    /// Gets or initializes the generator schema version.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Gets or initializes the generator rules version.
    /// </summary>
    public int RulesVersion { get; init; }

    /// <summary>
    /// Gets or initializes the custom-fusion target-pool metadata.
    /// </summary>
    public required VersionedPoolRecipePayload TargetPool { get; init; }
}

/// <summary>
/// Describes one versioned deterministic pool.
/// </summary>
public sealed class VersionedPoolRecipePayload
{
    /// <summary>
    /// Gets or initializes the pool schema version.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Gets or initializes the pool size.
    /// </summary>
    public int Size { get; init; }

    /// <summary>
    /// Gets or initializes the pool fingerprint.
    /// </summary>
    public required string Fingerprint { get; init; }
}

/// <summary>
/// Describes move-access-generator compatibility metadata.
/// </summary>
public sealed class MoveAccessGeneratorRecipePayload
{
    /// <summary>
    /// Gets or initializes the generator schema version.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Gets or initializes the permitted-move pool fingerprint.
    /// </summary>
    public required string PoolFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the contextual restriction fingerprint.
    /// </summary>
    public required string ContextualRestrictionFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the level-up source fingerprint.
    /// </summary>
    public required string LevelUpSourceFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the Egg source fingerprint.
    /// </summary>
    public required string EggSourceFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes TM source metadata.
    /// </summary>
    public required MachineSourceRecipePayload Tm { get; init; }

    /// <summary>
    /// Gets or initializes TR source metadata.
    /// </summary>
    public required MachineSourceRecipePayload Tr { get; init; }

    /// <summary>
    /// Gets or initializes ordinary tutor source metadata.
    /// </summary>
    public required TutorSourceRecipePayload Tutor { get; init; }

    /// <summary>
    /// Gets or initializes Fusion Tutor source metadata.
    /// </summary>
    public required TutorSourceRecipePayload FusionTutor { get; init; }
}

/// <summary>
/// Describes a machine roster and its compatibility source.
/// </summary>
public sealed class MachineSourceRecipePayload
{
    /// <summary>
    /// Gets or initializes the machine roster fingerprint.
    /// </summary>
    public required string RosterFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the compatibility source fingerprint.
    /// </summary>
    public required string SourceFingerprint { get; init; }
}

/// <summary>
/// Describes a tutor catalog and its compatibility source.
/// </summary>
public sealed class TutorSourceRecipePayload
{
    /// <summary>
    /// Gets or initializes the tutor catalog fingerprint.
    /// </summary>
    public required string CatalogFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the compatibility source fingerprint.
    /// </summary>
    public required string SourceFingerprint { get; init; }
}

/// <summary>
/// Describes player-fusion-generator compatibility metadata.
/// </summary>
public sealed class PlayerFusionGeneratorRecipePayload
{
    /// <summary>
    /// Gets or initializes the generator schema version.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Gets or initializes the custom-fusion result pool size.
    /// </summary>
    public int PoolSize { get; init; }

    /// <summary>
    /// Gets or initializes the custom-fusion result pool fingerprint.
    /// </summary>
    public required string PoolFingerprint { get; init; }
}
