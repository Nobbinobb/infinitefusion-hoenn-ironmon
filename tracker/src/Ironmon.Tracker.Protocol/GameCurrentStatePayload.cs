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
    /// <param name="battle">The active battle when one exists.</param>
    /// <param name="player">The initialized player Pokemon when one exists.</param>
    /// <param name="enemies">The legally visible active opposing Pokemon.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when sequence is negative.</exception>
    public GameCurrentStatePayload(bool ironmonActive, string? runId, string? battleId, long sequence, BattleSnapshot? battle = null, PlayerPokemonSnapshot? player = null, IReadOnlyList<EnemyPokemonSnapshot>? enemies = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        IronmonActive = ironmonActive;
        RunId = runId;
        BattleId = battleId;
        Sequence = sequence;
        Battle = battle;
        Player = player;
        Enemies = enemies ?? [];
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

    /// <summary>
    /// Gets the active battle when one exists.
    /// </summary>
    public BattleSnapshot? Battle { get; }

    /// <summary>
    /// Gets the most recently initialized player Pokemon when one exists.
    /// </summary>
    public PlayerPokemonSnapshot? Player { get; }

    /// <summary>
    /// Gets the legally visible active opposing Pokemon.
    /// </summary>
    public IReadOnlyList<EnemyPokemonSnapshot> Enemies { get; }
}
