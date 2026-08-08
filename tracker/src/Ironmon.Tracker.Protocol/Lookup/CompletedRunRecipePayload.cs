using System.Text.Json;

namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Carries the compact deterministic inputs needed to reconstruct one completed run.
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
    /// Gets or initializes the serialized run configuration.
    /// </summary>
    public JsonElement Configuration { get; init; }

    /// <summary>
    /// Gets or initializes the classic, remix, or expert data mode.
    /// </summary>
    public string DataMode { get; init; } = "classic";

    /// <summary>
    /// Gets or initializes the species generator schema version.
    /// </summary>
    public int SpeciesGeneratorVersion { get; init; }

    /// <summary>
    /// Gets or initializes the ability generator schema version.
    /// </summary>
    public int AbilityGeneratorVersion { get; init; }

    /// <summary>
    /// Gets or initializes the base-stat generator schema version when the run uses generated stats.
    /// </summary>
    public int? BaseStatGeneratorVersion { get; init; }

    /// <summary>
    /// Gets or initializes the player-fusion generator schema version.
    /// </summary>
    public int PlayerFusionGeneratorVersion { get; init; } = 2;

    /// <summary>
    /// Gets or initializes the normal-species pool fingerprint.
    /// </summary>
    public required string SpeciesPoolFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the ability-pool fingerprint.
    /// </summary>
    public required string AbilityPoolFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the source-stat fingerprint when the run uses generated stats.
    /// </summary>
    public string? BaseStatSourceFingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the custom-fusion pool fingerprint.
    /// </summary>
    public required string FusionPoolFingerprint { get; init; }
}
