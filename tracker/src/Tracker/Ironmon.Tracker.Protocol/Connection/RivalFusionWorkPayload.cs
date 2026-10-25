namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Prepares native fusion state or requests the rival's single ordered material pair.
/// </summary>
public sealed class RivalFusionWorkPayload
{
    /// <summary>
    /// Gets the supported rival fusion worker contract version.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Gets or initializes the game process session owning this work.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets or initializes the correlation identifier, or null for preparation without disclosed materials.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Gets or initializes the exact pinned generation inputs.
    /// </summary>
    public required FusionAssignmentRecipePayload Recipe { get; init; }

    /// <summary>
    /// Gets or initializes the normal body material, or null during preparation.
    /// </summary>
    public int? BodyId { get; init; }

    /// <summary>
    /// Gets or initializes the normal head material, or null during preparation.
    /// </summary>
    public int? HeadId { get; init; }
}
