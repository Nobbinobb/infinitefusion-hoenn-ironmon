namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Supplies the complete ordered reverse-pair table for optional active-gameplay preparation.
/// </summary>
/// <remarks>
/// Material-result selection and saved discoveries remain game-owned. The game validates the process,
/// deterministic recipe, and complete pool membership before installing this table.
/// </remarks>
public sealed class GameplayFusionPairingPayload
{
    /// <summary>
    /// Identifies the supported complete reverse-pair transport format.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Initializes an empty pairing assignment for protocol serialization.
    /// </summary>
    public GameplayFusionPairingPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the pairing transport version.
    /// </summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>
    /// Gets or initializes the destination game process session.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets or initializes the exact deterministic recipe validated by the native worker.
    /// </summary>
    public required FusionAssignmentRecipePayload Recipe { get; init; }

    /// <summary>
    /// Gets or initializes base64-encoded pairs of little-endian unsigned 32-bit species identifiers.
    /// </summary>
    /// <remarks>
    /// Pair order and orientation preserve the generator's tie-breaking order. Every custom-pool member occurs once.
    /// </remarks>
    public required string PackedPairs { get; init; }
}
