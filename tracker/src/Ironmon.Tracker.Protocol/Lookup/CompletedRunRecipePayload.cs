namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Carries the versioned deterministic inputs needed to reconstruct one completed run.
/// </summary>
public sealed class CompletedRunRecipePayload
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
    /// Gets or initializes the deterministic run seed.
    /// </summary>
    public long Seed { get; init; }

    /// <summary>
    /// Gets or initializes the run result identifier.
    /// </summary>
    public required string Result { get; init; }

    /// <summary>
    /// Gets or initializes the Infinite Fusion version.
    /// </summary>
    public required string GameVersion { get; init; }

    /// <summary>
    /// Gets or initializes the Ironmon version.
    /// </summary>
    public required string IronmonVersion { get; init; }

    /// <summary>
    /// Gets or initializes the typed run configuration.
    /// </summary>
    public required RunConfigurationPayload Configuration { get; init; }

    /// <summary>
    /// Gets or initializes the classic, remix, or expert data mode.
    /// </summary>
    public string DataMode { get; init; } = "classic";

    /// <summary>
    /// Gets or initializes species-generator compatibility metadata.
    /// </summary>
    public required SpeciesGeneratorRecipePayload SpeciesGenerator { get; init; }

    /// <summary>
    /// Gets or initializes ability-generator compatibility metadata.
    /// </summary>
    public required AbilityGeneratorRecipePayload AbilityGenerator { get; init; }

    /// <summary>
    /// Gets or initializes base-stat-generator compatibility metadata when enabled.
    /// </summary>
    public BaseStatGeneratorRecipePayload? BaseStatGenerator { get; init; }

    /// <summary>
    /// Gets or initializes evolution-generator compatibility metadata when enabled.
    /// </summary>
    public EvolutionGeneratorRecipePayload? EvolutionGenerator { get; init; }

    /// <summary>
    /// Gets or initializes move-access-generator compatibility metadata when enabled.
    /// </summary>
    public MoveAccessGeneratorRecipePayload? MoveAccessGenerator { get; init; }

    /// <summary>
    /// Gets or initializes player-fusion-generator compatibility metadata.
    /// </summary>
    public required PlayerFusionGeneratorRecipePayload PlayerFusionGenerator { get; init; }

    /// <summary>
    /// Gets or initializes authoritative attempt statistics when the recipe provides them.
    /// </summary>
    public RunStatisticsPayload? Statistics { get; init; }

    /// <summary>
    /// Gets or initializes locally observed move-access iteration data.
    /// </summary>
    public MoveAccessMetricsPayload? MoveAccessMetrics { get; init; }

    /// <summary>
    /// Gets or initializes locally observed generated-evolution outcomes.
    /// </summary>
    public EvolutionMetricsPayload? EvolutionMetrics { get; init; }
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
