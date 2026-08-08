using System.Text.Json;

namespace Ironmon.Tracker.Core.Runs;

/// <summary>
/// Describes the deterministic inputs required to reproduce a completed run.
/// </summary>
public sealed class CompletedRunRecipe
{
    /// <summary>
    /// Initializes a new completed-run recipe.
    /// </summary>
    /// <param name="runId">The stable run identifier.</param>
    /// <param name="seed">The deterministic run seed.</param>
    /// <param name="result">The result that ended the run.</param>
    /// <param name="gameVersion">The Infinite Fusion version.</param>
    /// <param name="ironmonVersion">The Ironmon version.</param>
    /// <param name="configuration">The run configuration.</param>
    /// <param name="speciesGeneratorVersion">The species generator schema.</param>
    /// <param name="abilityGeneratorVersion">The ability generator schema.</param>
    /// <param name="speciesPoolFingerprint">The species-pool fingerprint.</param>
    /// <param name="abilityPoolFingerprint">The ability-pool fingerprint.</param>
    /// <param name="fusionPoolFingerprint">The custom-fusion-pool fingerprint.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when a required identifier, version, or fingerprint is empty.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a generator version is not positive.
    /// </exception>
    public CompletedRunRecipe(
        string runId,
        long seed,
        RunResult result,
        string gameVersion,
        string ironmonVersion,
        JsonElement configuration,
        int speciesGeneratorVersion,
        int abilityGeneratorVersion,
        string speciesPoolFingerprint,
        string abilityPoolFingerprint,
        string fusionPoolFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(ironmonVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesPoolFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(abilityPoolFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(fusionPoolFingerprint);
        ArgumentOutOfRangeException.ThrowIfLessThan(speciesGeneratorVersion, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(abilityGeneratorVersion, 1);

        RunId = runId;
        Seed = seed;
        Result = result;
        GameVersion = gameVersion;
        IronmonVersion = ironmonVersion;
        Configuration = configuration.Clone();
        SpeciesGeneratorVersion = speciesGeneratorVersion;
        AbilityGeneratorVersion = abilityGeneratorVersion;
        SpeciesPoolFingerprint = speciesPoolFingerprint;
        AbilityPoolFingerprint = abilityPoolFingerprint;
        FusionPoolFingerprint = fusionPoolFingerprint;
    }

    /// <summary>
    /// Gets the stable run identifier.
    /// </summary>
    public string RunId { get; }

    /// <summary>
    /// Gets the deterministic run seed.
    /// </summary>
    public long Seed { get; }

    /// <summary>
    /// Gets the result that ended the run.
    /// </summary>
    public RunResult Result { get; }

    /// <summary>
    /// Gets the Infinite Fusion version used by the run.
    /// </summary>
    public string GameVersion { get; }

    /// <summary>
    /// Gets the Ironmon version used by the run.
    /// </summary>
    public string IronmonVersion { get; }

    /// <summary>
    /// Gets the serialized Ironmon run configuration.
    /// </summary>
    public JsonElement Configuration { get; }

    /// <summary>
    /// Gets the species generator schema version.
    /// </summary>
    public int SpeciesGeneratorVersion { get; }

    /// <summary>
    /// Gets the ability generator schema version.
    /// </summary>
    public int AbilityGeneratorVersion { get; }

    /// <summary>
    /// Gets the species-pool fingerprint.
    /// </summary>
    public string SpeciesPoolFingerprint { get; }

    /// <summary>
    /// Gets the ability-pool fingerprint.
    /// </summary>
    public string AbilityPoolFingerprint { get; }

    /// <summary>
    /// Gets the custom-fusion-pool fingerprint.
    /// </summary>
    public string FusionPoolFingerprint { get; }
}
