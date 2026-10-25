namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Describes the game state and save slots available to the tracker run manager.
/// </summary>
public sealed class GameManagementPayload
{
    /// <summary>
    /// Gets or initializes the game-management protocol version used by this snapshot.
    /// </summary>
    public int ProtocolVersion { get; init; }

    /// <summary>
    /// Gets or initializes whether the game is at the title screen, main menu, or in gameplay.
    /// </summary>
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the current title-screen or main-menu identifier, or null during gameplay.
    /// </summary>
    public string? MenuId { get; init; }

    /// <summary>
    /// Gets or initializes save-slot snapshots at the title screen or main menu, or an empty list during gameplay.
    /// </summary>
    public IReadOnlyList<GameSaveSlotPayload> Slots { get; init; } = [];
}

/// <summary>
/// Describes the occupancy, loadability, and saved-run metadata of one game save slot.
/// </summary>
public sealed class GameSaveSlotPayload
{
    /// <summary>
    /// Gets or initializes the game's manual or automatic save-slot identifier.
    /// </summary>
    public string Slot { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes whether this is a manual slot that can receive a new Ironmon run.
    /// </summary>
    public bool Manual { get; init; }

    /// <summary>
    /// Gets or initializes whether a save file occupies the slot.
    /// </summary>
    public bool Occupied { get; init; }

    /// <summary>
    /// Gets or initializes whether the saved metadata identifies an Ironmon run.
    /// </summary>
    public bool Ironmon { get; init; }

    /// <summary>
    /// Gets or initializes whether the save passes game validation and, for Ironmon, configuration-version checks.
    /// </summary>
    public bool Loadable { get; init; }

    /// <summary>
    /// Gets or initializes the save-content revision used to reject stale start or continue requests.
    /// </summary>
    public string Revision { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the saved trainer name.
    /// </summary>
    public string? Trainer { get; init; }

    /// <summary>
    /// Gets or initializes the saved location.
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Gets or initializes the saved badge count.
    /// </summary>
    public int Badges { get; init; }

    /// <summary>
    /// Gets or initializes the saved attempt number when available.
    /// </summary>
    public int? AttemptNumber { get; init; }

    /// <summary>
    /// Gets or initializes the saved Ironmon rules when the run is loadable.
    /// </summary>
    public IronmonRunRules? Rules { get; init; }
}

/// <summary>
/// Describes the run rules copied from a tracker profile or draft into an Ironmon save.
/// </summary>
public sealed record IronmonRunRules
{
    /// <summary>
    /// Gets or initializes the wild encounter species policy.
    /// </summary>
    public string WildPolicy { get; init; } = RunManagementProtocol.MixedPolicy;

    /// <summary>
    /// Gets or initializes the trainer species policy.
    /// </summary>
    public string TrainerPolicy { get; init; } = RunManagementProtocol.MixedPolicy;

    /// <summary>
    /// Gets or initializes the policy for the three starter offers shared by the player and rival.
    /// </summary>
    public string StarterPolicy { get; init; } = RunManagementProtocol.MixedPolicy;

    /// <summary>
    /// Gets or initializes the unfusion selection rule.
    /// </summary>
    public string UnfusionSetting { get; init; } = RunManagementProtocol.RandomUnfusion;
}

/// <summary>
/// Defines protocol identifiers for tracker-managed Ironmon runs and session heartbeats.
/// </summary>
public static class RunManagementProtocol
{
    /// <summary>
    /// Gets the supported game-management protocol version.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// Gets the command that requests game state and title-screen or main-menu save-slot snapshots.
    /// </summary>
    public const string SnapshotCommand = "game_management";

    /// <summary>
    /// Gets the command that requests a new Ironmon run in a manual save slot.
    /// </summary>
    public const string StartCommand = "start_ironmon";

    /// <summary>
    /// Gets the command that requests loading an existing Ironmon save.
    /// </summary>
    public const string ContinueCommand = "continue_ironmon";

    /// <summary>
    /// Gets the event that keeps the connected game session eligible for Ironmon play.
    /// </summary>
    public const string HeartbeatEvent = "tracker_heartbeat";

    /// <summary>
    /// Gets the state identifier indicating that the game can accept start or continue requests.
    /// </summary>
    public const string MainMenuState = "main_menu";

    /// <summary>
    /// Gets the state identifier for the title screen awaiting player input.
    /// </summary>
    public const string TitleScreenState = "title_screen";

    /// <summary>
    /// Gets the species-policy identifier that allows normal Pokémon and custom fusions.
    /// </summary>
    public const string MixedPolicy = "mixed";

    /// <summary>
    /// Gets the species-policy identifier that allows only custom fusions.
    /// </summary>
    public const string FusionPolicy = "custom_fusions_only";

    /// <summary>
    /// Gets the species-policy identifier that allows only normal Pokémon.
    /// </summary>
    public const string NormalPolicy = "normal_only";

    /// <summary>
    /// Gets the unfusion-rule identifier that selects the surviving component randomly.
    /// </summary>
    public const string RandomUnfusion = "random_component";

    /// <summary>
    /// Gets the unfusion-rule identifier that lets the player select the surviving component.
    /// </summary>
    public const string ChooseUnfusion = "player_choice";
}

/// <summary>
/// Requests a new or continued Ironmon run against the reviewed title screen or main menu and save revision.
/// </summary>
public sealed class GameManagementRequestPayload
{
    /// <summary>
    /// Gets or initializes the reviewed title-screen or main-menu identifier required by the game.
    /// </summary>
    public string? MenuId { get; init; }

    /// <summary>
    /// Gets or initializes the selected manual or automatic save-slot identifier.
    /// </summary>
    public string Slot { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the reviewed save-content revision required by the game.
    /// </summary>
    public string Revision { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes whether the player confirmed replacing an occupied manual slot for a new run.
    /// </summary>
    public bool Overwrite { get; init; }

    /// <summary>
    /// Gets or initializes the rules for a new run, or null when continuing a saved run.
    /// </summary>
    public IronmonRunRules? Rules { get; init; }
}

/// <summary>
/// Reports whether the game queued the requested title action.
/// </summary>
public sealed class GameManagementResponsePayload
{
    /// <summary>
    /// Gets or initializes whether the request was queued.
    /// </summary>
    public bool Accepted { get; init; }
}
