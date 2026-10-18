namespace Ironmon.Tracker.App.Controls;

/// <summary>
/// Defines the game and tracker actions offered by the controls editor.
/// </summary>
public static class ControlCatalog
{
    /// <summary>
    /// Identifies the up game action.
    /// </summary>
    public const string Up = "up";

    /// <summary>
    /// Identifies the down game action.
    /// </summary>
    public const string Down = "down";

    /// <summary>
    /// Identifies the left game action.
    /// </summary>
    public const string Left = "left";

    /// <summary>
    /// Identifies the right game action.
    /// </summary>
    public const string Right = "right";

    /// <summary>
    /// Identifies the confirm game action.
    /// </summary>
    public const string Confirm = "confirm";

    /// <summary>
    /// Identifies the cancel game action.
    /// </summary>
    public const string Cancel = "cancel";

    /// <summary>
    /// Identifies interaction with field objects and events.
    /// </summary>
    public const string Interact = "interact";

    /// <summary>
    /// Identifies opening the field pause menu.
    /// </summary>
    public const string Menu = "menu";

    /// <summary>
    /// Identifies the Ironmon wild-battle Run shortcut.
    /// </summary>
    public const string BattleRun = "battle_run";

    /// <summary>
    /// Identifies selecting the previous battle quick item.
    /// </summary>
    public const string QuickItemLeft = "quick_item_left";

    /// <summary>
    /// Identifies selecting the next battle quick item.
    /// </summary>
    public const string QuickItemRight = "quick_item_right";

    /// <summary>
    /// Identifies selecting the previous battle item category.
    /// </summary>
    public const string QuickItemUp = "quick_item_up";

    /// <summary>
    /// Identifies selecting the next battle item category.
    /// </summary>
    public const string QuickItemDown = "quick_item_down";

    /// <summary>
    /// Identifies using the selected battle quick item.
    /// </summary>
    public const string QuickItemUse = "quick_item_use";

    /// <summary>
    /// Identifies the action game action.
    /// </summary>
    public const string Action = "action";

    /// <summary>
    /// Identifies the speed game action.
    /// </summary>
    public const string Speed = "speed";

    /// <summary>
    /// Identifies the auxiliary game action.
    /// </summary>
    public const string Auxiliary = "auxiliary";

    /// <summary>
    /// Identifies the ready game action.
    /// </summary>
    public const string Ready = "ready";

    /// <summary>
    /// Identifies the page_up game action.
    /// </summary>
    public const string PageUp = "page_up";

    /// <summary>
    /// Identifies the page_down game action.
    /// </summary>
    public const string PageDown = "page_down";

    /// <summary>
    /// Identifies the screenshot game action.
    /// </summary>
    public const string Screenshot = "screenshot";

    /// <summary>
    /// Identifies the player tracker shortcut.
    /// </summary>
    public const string Player = "tracker_player";

    /// <summary>
    /// Identifies the enemy tracker shortcut.
    /// </summary>
    public const string Enemy = "tracker_enemy";

    /// <summary>
    /// Identifies the lookup tracker shortcut.
    /// </summary>
    public const string Lookup = "tracker_lookup";

    /// <summary>
    /// Identifies the archive tracker shortcut.
    /// </summary>
    public const string Archive = "tracker_archive";

    /// <summary>
    /// Identifies the guarded Ironmon reset action.
    /// </summary>
    public const string Reset = "reset";

    /// <summary>
    /// Gets the ordered editable action definitions.
    /// </summary>
    public static IReadOnlyList<ControlAction> Actions { get; } =
    [
        new(Up, "Move up", [[0x26]], [[ControlInput.LeftYNegative], [ControlInput.DpadUp]]),
        new(Down, "Move down", [[0x28]], [[ControlInput.LeftYPositive], [ControlInput.DpadDown]]),
        new(Left, "Move left", [[0x25]], [[ControlInput.LeftXNegative], [ControlInput.DpadLeft]]),
        new(Right, "Move right", [[0x27]], [[ControlInput.LeftXPositive], [ControlInput.DpadRight]]),
        new(Interact, "Interact", [[0x43], [0x0D]], [[ControlInput.South]]),
        new(Menu, "Open pause menu", [[0x58], [0x1B]], [[ControlInput.East]]),
        new(Confirm, "Confirm", [[0x43], [0x0D]], [[ControlInput.South]]),
        new(Cancel, "Cancel / back", [[0x58], [0x1B]], [[ControlInput.East]]),
        new(BattleRun, "Run from battle", [[0x58], [0x1B]], [[ControlInput.East]]),
        new(QuickItemLeft, "Previous quick item", [[0x11, 0x10, 0x25]], [[ControlInput.LeftTrigger, ControlInput.RightTrigger, ControlInput.DpadLeft]]),
        new(QuickItemRight, "Next quick item", [[0x11, 0x10, 0x27]], [[ControlInput.LeftTrigger, ControlInput.RightTrigger, ControlInput.DpadRight]]),
        new(QuickItemUp, "Previous item category", [[0x11, 0x10, 0x26]], [[ControlInput.LeftTrigger, ControlInput.RightTrigger, ControlInput.DpadUp]]),
        new(QuickItemDown, "Next item category", [[0x11, 0x10, 0x28]], [[ControlInput.LeftTrigger, ControlInput.RightTrigger, ControlInput.DpadDown]]),
        new(QuickItemUse, "Use quick item", [[0x11, 0x10, 0x43]], [[ControlInput.LeftTrigger, ControlInput.RightTrigger, ControlInput.South]]),
        new(Action, "Run / action", [[0x5A], [0x10]], [[ControlInput.West]]),
        new(Speed, "Game speed", [[0x41]], [[ControlInput.North]]),
        new(Auxiliary, "Auxiliary (Y)", [[0x53]], [[ControlInput.LeftStick]]),
        new(Ready, "Registered items", [[0x44]], [[ControlInput.RightStick]]),
        new(PageUp, "Previous page", [[0x51], [0x21]], [[ControlInput.LeftShoulder]]),
        new(PageDown, "Next page", [[0x57], [0x22]], [[ControlInput.RightShoulder]]),
        new(Screenshot, "Screenshot", [[0x77]], []),
        new(Reset, "Reset Ironmon attempt", [[0x76]], [[ControlInput.LeftTrigger, ControlInput.RightTrigger, ControlInput.Start]]),
        new(Player, "Player", [[0x11, 0x31]], [[ControlInput.LeftTrigger, ControlInput.RightTrigger, ControlInput.RightXNegative]]),
        new(Enemy, "Enemy / cycle opponent", [[0x11, 0x32]], [[ControlInput.LeftTrigger, ControlInput.RightTrigger, ControlInput.RightXPositive]]),
        new(Lookup, "Lookup", [[0x11, 0x33]], [[ControlInput.LeftTrigger, ControlInput.RightTrigger, ControlInput.RightYNegative]]),
        new(Archive, "Archive", [[0x11, 0x34]], [[ControlInput.LeftTrigger, ControlInput.RightTrigger, ControlInput.RightYPositive]])
    ];

    /// <summary>
    /// Gets the categories and the actions displayed once in each category.
    /// </summary>
    public static IReadOnlyList<ControlGroup> Groups { get; } =
    [
        new("Movement", [Up, Down, Left, Right], "Move around the field and navigate menus."),
        new("Field", [Interact, Menu, Action, Ready], "Controls used while exploring the field."),
        new("Menus", [Confirm, Cancel, PageUp, PageDown], "Confirm and cancel also apply to dialogs and battle menus."),
        new("Battle", [BattleRun, QuickItemLeft, QuickItemRight, QuickItemUp, QuickItemDown, QuickItemUse], "Browse quick items and categories at the battle command or move menu, then use the selected item. Each category remembers its selection between battles. Run applies to wild battles at the main command menu."),
        new("Tracker", [Reset, Player, Enemy, Lookup, Archive], "Ironmon reset and tracker navigation."),
        new("System", [Speed, Auxiliary, Screenshot], "Game speed and other system controls.")
    ];

    /// <summary>
    /// Gets the standard mapped controls offered by the binding dialog.
    /// </summary>
    public static IReadOnlyList<string> ControllerInputs { get; } =
    [
        ControlInput.South, ControlInput.East, ControlInput.West, ControlInput.North,
        ControlInput.LeftShoulder, ControlInput.RightShoulder, ControlInput.LeftTrigger, ControlInput.RightTrigger,
        ControlInput.Back, ControlInput.Start, ControlInput.LeftStick, ControlInput.RightStick,
        ControlInput.DpadUp, ControlInput.DpadDown, ControlInput.DpadLeft, ControlInput.DpadRight,
        ControlInput.LeftYNegative, ControlInput.LeftYPositive, ControlInput.LeftXNegative, ControlInput.LeftXPositive,
        ControlInput.RightYNegative, ControlInput.RightYPositive, ControlInput.RightXNegative, ControlInput.RightXPositive
    ];

    /// <summary>
    /// Gets all game action identifiers, including standalone reset.
    /// </summary>
    public static string[] GameActions => [.. Actions.Select(action => action.Id).Where(id => !IsTrackerAction(id))];

    /// <summary>
    /// Determines whether an action belongs to tracker navigation.
    /// </summary>
    /// <param name="id">The action identifier to inspect.</param>
    /// <returns>Whether the action selects a tracker view.</returns>
    public static bool IsTrackerAction(string id) => id is Player or Enemy or Lookup or Archive;

    /// <summary>
    /// Determines whether two contextual actions can respond to the same game input query.
    /// </summary>
    /// <param name="first">The first action identifier.</param>
    /// <param name="second">The second action identifier.</param>
    /// <returns>Whether a longer chord may consume the other action's component inputs.</returns>
    public static bool ShareInputContext(string first, string second)
    {
        bool fieldAndMenu = (first is Interact or Menu && second is Confirm or Cancel or BattleRun)
            || (second is Interact or Menu && first is Confirm or Cancel or BattleRun);
        bool cancelAndRun = (first == Cancel && second == BattleRun) || (first == BattleRun && second == Cancel);
        return !fieldAndMenu && !cancelAndRun;
    }
}

/// <summary>
/// Groups related controls into one editor tab.
/// </summary>
/// <remarks>
/// Initializes a category with its action identifiers and contextual help.
/// </remarks>
/// <param name="label">The category name.</param>
/// <param name="actions">The ordered action identifiers.</param>
/// <param name="description">Help explaining the action context.</param>
public sealed class ControlGroup(string label, string[] actions, string description)
{
    /// <summary>
    /// Gets the category name.
    /// </summary>
    public string Label { get; } = label;

    /// <summary>
    /// Gets the ordered action identifiers.
    /// </summary>
    public string[] Actions { get; } = actions;

    /// <summary>
    /// Gets the contextual help.
    /// </summary>
    public string Description { get; } = description;
}

/// <summary>
/// Describes one action and its initial keyboard and mapped-gamepad bindings.
/// </summary>
/// <remarks>
/// Initializes a control definition.
/// </remarks>
/// <param name="id">The stable action identifier.</param>
/// <param name="label">The display label.</param>
/// <param name="keyboard">Alternative keyboard chords.</param>
/// <param name="controller">Alternative controller chords.</param>
public sealed class ControlAction(string id, string label, int[][] keyboard, string[][] controller)
{
    /// <summary>
    /// Gets the stable identifier.
    /// </summary>
    public string Id { get; } = id;

    /// <summary>
    /// Gets the display label.
    /// </summary>
    public string Label { get; } = label;

    /// <summary>
    /// Gets the initial keyboard alternatives.
    /// </summary>
    public int[][] Keyboard { get; } = keyboard;

    /// <summary>
    /// Gets the initial mapped-controller alternatives.
    /// </summary>
    public string[][] Controller { get; } = controller;
}
