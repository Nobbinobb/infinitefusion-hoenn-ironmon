namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Describes the connection-level game state returned during recovery.
/// </summary>
public sealed class GameCurrentStatePayload
{
    /// <summary>
    /// Initializes a connection-level game state payload.
    /// </summary>
    /// <param name="ironmonActive">Whether Ironmon mode is active.</param>
    /// <param name="runId">The current run identifier.</param>
    /// <param name="battleId">The current battle identifier.</param>
    /// <param name="sequence">The latest game event sequence.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when sequence is negative.</exception>
    public GameCurrentStatePayload(bool ironmonActive, string? runId, string? battleId, long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        IronmonActive = ironmonActive;
        RunId = runId;
        BattleId = battleId;
        Sequence = sequence;
    }

    /// <summary>
    /// Gets whether Ironmon mode is active.
    /// </summary>
    public bool IronmonActive { get; }

    /// <summary>
    /// Gets the current run identifier when one exists.
    /// </summary>
    public string? RunId { get; }

    /// <summary>
    /// Gets the current battle identifier when one exists.
    /// </summary>
    public string? BattleId { get; }

    /// <summary>
    /// Gets the latest game event sequence.
    /// </summary>
    public long Sequence { get; }
}
