namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Returns one native rival fusion assignment to the requesting game session.
/// </summary>
public sealed class RivalFusionResultPayload
{
    /// <summary>
    /// Gets or initializes the originating game process session.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets or initializes the originating request identifier.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// Gets or initializes the ordered body material.
    /// </summary>
    public int BodyId { get; init; }

    /// <summary>
    /// Gets or initializes the ordered head material.
    /// </summary>
    public int HeadId { get; init; }

    /// <summary>
    /// Gets or initializes the assigned custom fusion, or null when preparation failed.
    /// </summary>
    public int? SpeciesNumber { get; init; }

    /// <summary>
    /// Gets or initializes a preparation failure, or null on success.
    /// </summary>
    public string? Error { get; init; }
}
