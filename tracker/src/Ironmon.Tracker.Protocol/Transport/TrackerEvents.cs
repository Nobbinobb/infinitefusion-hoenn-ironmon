namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Defines stable event identifiers exchanged by the game and tracker.
/// </summary>
public static class TrackerEvents
{
    /// <summary>
    /// Gets the game handshake event.
    /// </summary>
    public const string GameConnected = "game_connected";

    /// <summary>
    /// Gets the tracker handshake event.
    /// </summary>
    public const string TrackerConnected = "tracker_connected";

    /// <summary>
    /// Gets the new-run event.
    /// </summary>
    public const string RunStarted = "run_started";

    /// <summary>
    /// Gets the completed-run event.
    /// </summary>
    public const string RunCompleted = "run_completed";

    /// <summary>
    /// Gets the game-to-tracker area-discovery package event.
    /// </summary>
    public const string AreaDiscovery = "area_discovery";

    /// <summary>
    /// Gets the tracker-to-game persisted-discovery acknowledgment event.
    /// </summary>
    public const string AreaDiscoveryAcknowledged = "area_discovery_acknowledged";

    /// <summary>
    /// Gets the starter-selection state event.
    /// </summary>
    public const string StarterSelectionChanged = "starter_selection_changed";

    /// <summary>
    /// Gets the battle-start event.
    /// </summary>
    public const string BattleStarted = "battle_started";

    /// <summary>
    /// Gets the battle-end event.
    /// </summary>
    public const string BattleEnded = "battle_ended";

    /// <summary>
    /// Gets the initial player send-out event.
    /// </summary>
    public const string PlayerSentOut = "player_sent_out";

    /// <summary>
    /// Gets the player-state update event.
    /// </summary>
    public const string PlayerStateChanged = "player_state_changed";

    /// <summary>
    /// Gets the player move-menu navigation event.
    /// </summary>
    public const string PlayerMoveMenuOpened = "player_move_menu_opened";

    /// <summary>
    /// Gets the initial enemy send-out event.
    /// </summary>
    public const string EnemySentOut = "enemy_sent_out";

    /// <summary>
    /// Gets the enemy-state update event.
    /// </summary>
    public const string EnemyStateChanged = "enemy_state_changed";

    /// <summary>
    /// Gets the observed enemy-move event.
    /// </summary>
    public const string EnemyMoveUsed = "enemy_move_used";

    /// <summary>
    /// Gets the revealed enemy-ability event.
    /// </summary>
    public const string EnemyAbilityRevealed = "enemy_ability_revealed";
}
