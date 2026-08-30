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
    /// Gets the live tracker-to-game diagnostic-access replacement event.
    /// </summary>
    public const string DiagnosticAccessChanged = "diagnostic_access_changed";

    /// <summary>
    /// Gets the new-run event.
    /// </summary>
    public const string RunStarted = "run_started";

    /// <summary>
    /// Gets the event announcing that an active run may begin background preparation.
    /// </summary>
    public const string ActiveRunPreparationReady = "active_run_preparation_ready";

    /// <summary>
    /// Gets the completed-run event.
    /// </summary>
    public const string RunCompleted = "run_completed";

    /// <summary>
    /// Gets a queued, started, or failed seeded-run import lifecycle event.
    /// </summary>
    public const string SeededRunImportStatus = "seeded_run_import_status";

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
    /// Gets the player battle-target navigation event.
    /// </summary>
    public const string PlayerTargetChanged = "player_target_changed";

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
