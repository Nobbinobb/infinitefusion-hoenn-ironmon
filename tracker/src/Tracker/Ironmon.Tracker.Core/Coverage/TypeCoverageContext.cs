namespace Ironmon.Tracker.Core.Coverage;

/// <summary>
/// Describes the connected release and population metadata needed to authorize aggregate coverage calculations.
/// </summary>
/// <remarks>
/// Initializes one connected type-coverage context.
/// </remarks>
/// <param name="gameVersion">The connected Infinite Fusion version.</param>
/// <param name="policy">The active trainer species policy.</param>
/// <param name="normalPoolSize">The connected eligible normal-species pool size.</param>
/// <param name="normalPoolFingerprint">The connected eligible normal-species pool fingerprint.</param>
/// <param name="fusionPoolSchemaVersion">The connected custom-fusion pool schema version.</param>
/// <param name="fusionPoolSize">The connected eligible custom-fusion pool size.</param>
/// <param name="fusionPoolFingerprint">The connected eligible custom-fusion pool fingerprint.</param>
public sealed class TypeCoverageContext(string gameVersion, TypeCoveragePolicy policy, int normalPoolSize, string normalPoolFingerprint, int fusionPoolSchemaVersion, int fusionPoolSize, string fusionPoolFingerprint)
{
    /// <summary>
    /// Gets the connected Infinite Fusion version.
    /// </summary>
    public string GameVersion { get; } = gameVersion;

    /// <summary>
    /// Gets the active trainer species policy.
    /// </summary>
    public TypeCoveragePolicy Policy { get; } = policy;

    /// <summary>
    /// Gets the connected eligible normal-species pool size.
    /// </summary>
    public int NormalPoolSize { get; } = normalPoolSize;

    /// <summary>
    /// Gets the connected eligible normal-species pool fingerprint.
    /// </summary>
    public string NormalPoolFingerprint { get; } = normalPoolFingerprint;

    /// <summary>
    /// Gets the connected custom-fusion pool schema version.
    /// </summary>
    public int FusionPoolSchemaVersion { get; } = fusionPoolSchemaVersion;

    /// <summary>
    /// Gets the connected eligible custom-fusion pool size.
    /// </summary>
    public int FusionPoolSize { get; } = fusionPoolSize;

    /// <summary>
    /// Gets the connected eligible custom-fusion pool fingerprint.
    /// </summary>
    public string FusionPoolFingerprint { get; } = fusionPoolFingerprint;
}
