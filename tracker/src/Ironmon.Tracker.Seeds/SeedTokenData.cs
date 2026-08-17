namespace Ironmon.Tracker.Seeds;

/// <summary>
/// Carries signature-verified, normalized inputs for one seeded run.
/// </summary>
public sealed class SeedTokenData
{
    /// <summary>
    /// Gets or initializes the token identity.
    /// </summary>
    public required string TokenId { get; init; }

    /// <summary>
    /// Gets or initializes the token issue instant.
    /// </summary>
    public DateTimeOffset IssuedAt { get; init; }

    /// <summary>
    /// Gets or initializes the deterministic run seed.
    /// </summary>
    public long Seed { get; init; }

    /// <summary>
    /// Gets or initializes the Infinite Fusion version.
    /// </summary>
    public required string GameVersion { get; init; }

    /// <summary>
    /// Gets or initializes the Ironmon version.
    /// </summary>
    public required string IronmonVersion { get; init; }

    /// <summary>
    /// Gets or initializes the game-data mode.
    /// </summary>
    public required string DataMode { get; init; }

    /// <summary>
    /// Gets or initializes the complete run configuration.
    /// </summary>
    public required RunConfigurationPayload Configuration { get; init; }

    /// <summary>
    /// Gets or initializes the canonical installation compatibility fingerprint.
    /// </summary>
    public required string CompatibilityFingerprint { get; init; }
}
