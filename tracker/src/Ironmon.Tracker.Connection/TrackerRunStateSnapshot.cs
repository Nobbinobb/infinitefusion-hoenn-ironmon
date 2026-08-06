using Ironmon.Tracker.Protocol;

namespace Ironmon.Tracker.Connection;

/// <summary>
/// Represents the live battle and player data currently held by the tracker.
/// </summary>
public sealed class TrackerRunStateSnapshot
{
    /// <summary>
    /// Initializes a tracker run-state snapshot.
    /// </summary>
    /// <param name="battle">The active battle when one exists.</param>
    /// <param name="player">The initialized player Pokemon when one exists.</param>
    public TrackerRunStateSnapshot(BattleSnapshot? battle, PlayerPokemonSnapshot? player)
    {
        Battle = battle;
        Player = player;
    }

    /// <summary>
    /// Gets the active battle when one exists.
    /// </summary>
    public BattleSnapshot? Battle { get; }

    /// <summary>
    /// Gets the initialized player Pokemon when one exists.
    /// </summary>
    public PlayerPokemonSnapshot? Player { get; }
}
