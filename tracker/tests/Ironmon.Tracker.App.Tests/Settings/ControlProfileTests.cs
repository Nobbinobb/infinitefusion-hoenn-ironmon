using Hexa.NET.SDL2;
using Ironmon.Tracker.App.Components.Navigation;
using Ironmon.Tracker.App.Controls;
using Ironmon.Tracker.Connection.Areas;
using Ironmon.Tracker.Connection.CompletedRuns;
using Ironmon.Tracker.Connection.Diagnostics;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.RunState;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Connection;
using Ironmon.Tracker.Protocol.Transport;
using Microsoft.AspNetCore.Components.Web;
using Xunit.Abstractions;

namespace Ironmon.Tracker.App.Tests.Settings;

/// <summary>
/// Verifies device isolation, defaults, persistence, and overlapping chord priority.
/// </summary>
/// <remarks>
/// Initializes controller tests with their diagnostic output.
/// </remarks>
/// <param name="output">The test diagnostic output.</param>
public sealed class ControlProfileTests(ITestOutputHelper output)
{
    private const string _xboxIdentity = "xbox-device";
    private const string _otherIdentity = "other-device";
    private const string _confirm = "confirm";
    private const string _leftTrigger = "left_trigger";
    private const string _rightTrigger = "right_trigger";
    private const string _start = "start";
    private const string _rawButton = "button_3";
    private const string _enabledProperty = "enabled";
    private const string _managedProperty = "managed";
    private const string _pressedProperty = "pressed";
    private const string _controlKey = "Control";
    private const string _controlCode = "ControlLeft";
    private const string _eKey = "e";
    private const string _eCode = "KeyE";
    private const string _oneKey = "1";
    private const string _oneCode = "Digit1";
    private const string _version = "test";
    private const string _corruptCollections = "{\"Keyboard\":null,\"Controllers\":null}";
    private const string _controllerSeparator = ", ";
    private const string _decimalKey = ".";
    private const string _decimalCode = "NumpadDecimal";
    private const string _addKey = "+";
    private const string _addCode = "NumpadAdd";
    private const string _rawAxis = "axis_0+";
    private const string _rawTestButton = "button_1";
    private const string _rawHat = "hat_0:up";
    private const string _altKey = "Alt";
    private const string _altCode = "AltLeft";
    private const string _enterKey = "Enter";
    private const string _captureMethod = "Capture";

    /// <summary>
    /// Uses either custom game binding without retaining the native keyboard or controller defaults for that action.
    /// </summary>
    [Fact]
    public void BothGameBindingSlotsReplaceTheActionsNativeDefaults()
    {
        ControlProfiles profiles = new(() => null, _ => { });
        profiles.Remember(_xboxIdentity, "Xbox", true);
        profiles.SetKeyboardSlot(ControlCatalog.Confirm, 0, [0x45]);
        profiles.SetKeyboardSlot(ControlCatalog.Confirm, 1, [0x56]);
        profiles.SetControllerSlot(_xboxIdentity, ControlCatalog.Confirm, 0, [ControlInput.Back]);
        profiles.SetControllerSlot(_xboxIdentity, ControlCatalog.Confirm, 1, [ControlInput.Start]);
        Dictionary<string, int[][]> keyboard = ControlCatalog.Actions.ToDictionary(action => action.Id, profiles.Keyboard);
        Dictionary<string, string[][]> controller = ControlCatalog.Actions.ToDictionary(action => action.Id, action => profiles.Controller(action, _xboxIdentity, true));
        foreach (int key in new[] { 0x45, 0x56 })
            Assert.Contains(ControlCatalog.Confirm, ControlMatcher.Match(keyboard, new HashSet<int> { key }));

        foreach (int key in new[] { 0x43, 0x0D })
            Assert.DoesNotContain(ControlCatalog.Confirm, ControlMatcher.Match(keyboard, new HashSet<int> { key }));

        foreach (string token in new[] { ControlInput.Back, ControlInput.Start })
            Assert.Contains(ControlCatalog.Confirm, ControlMatcher.Match(controller, new HashSet<string> { token }));

        Assert.DoesNotContain(ControlCatalog.Confirm, ControlMatcher.Match(controller, new HashSet<string> { ControlInput.South }));
    }

    /// <summary>
    /// Clears both conflicting old alternatives when the same chord was stored in both slots.
    /// </summary>
    [Fact]
    public void AllConflictingOldSlotsAreUnboundTogether()
    {
        ControlProfiles profiles = new(() => null, _ => { });
        profiles.SetKeyboard(ControlCatalog.Menu, [[0x45], [0x45]]);
        IReadOnlyList<ControlBindingSlot> conflicts = profiles.SetKeyboardSlot(ControlCatalog.Reset, 1, [0x45]);
        Assert.Equal([0, 1], conflicts.Select(binding => binding.Slot));
        Assert.All(conflicts, binding => Assert.Equal(ControlCatalog.Menu, binding.Action));
        Assert.All(profiles.Keyboard(ControlCatalog.Actions.Single(action => action.Id == ControlCatalog.Menu)), Assert.Empty);
    }

    /// <summary>
    /// Shows controller conflict warnings only after a successful transaction and preserves old controls on failure.
    /// </summary>
    /// <returns>A task that completes after the capture transport is disposed.</returns>
    [Trait(TrackerTestCategories.Category, TrackerTestCategories.GameCatalog)]
    [Fact]
    public async Task ControllerCaptureWarnsBeforeReplacementAndAfterSuccessfulSave()
    {
        using TestWorkspace workspace = new();
        TrackerKnowledgeOptions storage = new(workspace.Root);
        TrackerConnectionState state = new();
        await using TrackerConnectionService connection = new(new TrackerConnectionOptions(0, _version, false, TimeSpan.FromSeconds(2)), new TrackerDiagnosticsStore(), state, new TrackerRunState(), new TrackerKnowledgeStore(storage), new AreaDiscoveryStore(storage), new CompletedRunArchive(storage));
        bool failWrites = false;
        ControlProfiles profiles = new(() => null, _ =>
        {
            if (failWrites)
                throw new IOException("Test preference failure.");
        });
        profiles.Remember(_xboxIdentity, "Xbox", true);
        TrackerGlobalShortcutService shortcuts = new(state, connection, profiles);
        shortcuts.BeginCapture(ControlCatalog.Reset, _xboxIdentity, 1, confirm: true);
        shortcuts.PreviewController([ControlInput.East]);
        Assert.Contains("Open pause menu (Binding 1)", shortcuts.CaptureWarning);
        Assert.Contains("Cancel / back (Binding 1)", shortcuts.CaptureWarning);
        Assert.Contains("Run from battle (Binding 1)", shortcuts.CaptureWarning);
        Assert.Null(shortcuts.BindingWarning);
        failWrites = true;
        Assert.False(shortcuts.CommitCapture());
        Assert.NotNull(shortcuts.BindingError);
        Assert.Null(shortcuts.BindingWarning);
        ControlAction menu = ControlCatalog.Actions.Single(action => action.Id == ControlCatalog.Menu);
        Assert.Equal(menu.Controller, profiles.Controller(menu, _xboxIdentity, true));
        failWrites = false;
        Assert.True(shortcuts.CommitCapture());
        Assert.Null(shortcuts.BindingError);
        Assert.Null(shortcuts.CaptureWarning);
        Assert.Contains("Unbound: Open pause menu (Binding 1)", shortcuts.BindingWarning);
        Assert.Empty(profiles.Controller(menu, _xboxIdentity, true)[0]);
        shortcuts.NotifyBindingsChanged();
        Assert.Null(shortcuts.BindingWarning);
    }

    /// <summary>
    /// Moves an exact keyboard conflict into either slot while preserving unaffected alternatives after reload.
    /// </summary>
    /// <param name="slot">The receiving reset slot.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void KeyboardAssignmentsUnbindOldConflictingSlots(int slot)
    {
        string? stored = null;
        ControlProfiles profiles = new(() => stored, value => stored = value);
        ControlAction reset = ControlCatalog.Actions.Single(action => action.Id == ControlCatalog.Reset);
        Assert.Empty(profiles.ConflictingSlots(null));
        IReadOnlyList<ControlBindingSlot> preview = profiles.KeyboardConflicts(reset.Id, [0x1B]);
        Assert.Equal(3, preview.Count);
        Assert.All(preview, binding => Assert.Equal(1, binding.Slot));
        IReadOnlyList<ControlBindingSlot> unbound = profiles.SetKeyboardSlot(reset.Id, slot, [0x1B]);
        Assert.Equal(preview.Select(binding => (binding.Action, binding.Slot)), unbound.Select(binding => (binding.Action, binding.Slot)));
        ControlProfiles reloaded = new(() => stored, _ => { });
        Assert.Equal([0x1B], reloaded.Keyboard(reset)[slot]);
        foreach (string id in new[] { ControlCatalog.Menu, ControlCatalog.Cancel, ControlCatalog.BattleRun })
        {
            ControlAction action = ControlCatalog.Actions.Single(item => item.Id == id);
            Assert.Equal([0x58], reloaded.Keyboard(action)[0]);
            Assert.Empty(reloaded.Keyboard(action)[1]);
        }

        Assert.Empty(reloaded.ConflictingSlots(null));
        Assert.Equal([reset.Id], ControlMatcher.Match(ControlCatalog.Actions.ToDictionary(action => action.Id, reloaded.Keyboard), new HashSet<int> { 0x1B }));
        IReadOnlyList<ControlBindingSlot> reverse = reloaded.SetKeyboardSlot(ControlCatalog.Menu, 1, [0x1B]);
        Assert.Single(reverse);
        Assert.Equal(reset.Id, reverse[0].Action);
        Assert.Equal(slot, reverse[0].Slot);
        Assert.Empty(reloaded.Keyboard(reset)[slot]);
    }

    /// <summary>
    /// Limits conflict replacement to one controller and preserves the keyboard and other controller profiles.
    /// </summary>
    /// <param name="slot">The receiving controller reset slot.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ControllerAssignmentsMoveConflictsOnlyWithinTheSelectedProfile(int slot)
    {
        string? stored = null;
        ControlProfiles profiles = new(() => stored, value => stored = value);
        profiles.Remember(_xboxIdentity, "Xbox", true);
        profiles.Remember(_otherIdentity, "DualSense", true);
        ControlAction reset = ControlCatalog.Actions.Single(action => action.Id == ControlCatalog.Reset);
        Assert.Empty(profiles.ConflictingSlots(_xboxIdentity));
        Assert.Equal(3, profiles.ControllerConflicts(_xboxIdentity, reset.Id, [ControlInput.East]).Count);
        Assert.Equal(3, profiles.SetControllerSlot(_xboxIdentity, reset.Id, slot, [ControlInput.East]).Count);
        ControlProfiles reloaded = new(() => stored, _ => { });
        foreach (string id in new[] { ControlCatalog.Menu, ControlCatalog.Cancel, ControlCatalog.BattleRun })
        {
            ControlAction action = ControlCatalog.Actions.Single(item => item.Id == id);
            Assert.Empty(reloaded.Controller(action, _xboxIdentity, true)[0]);
            Assert.Equal(action.Controller, reloaded.Controller(action, _otherIdentity, true));
            Assert.Equal(action.Keyboard, reloaded.Keyboard(action));
        }

        Assert.Equal([ControlInput.East], reloaded.Controller(reset, _xboxIdentity, true)[slot]);
        Assert.Equal([reset.Id], ControlMatcher.Match(ControlCatalog.Actions.ToDictionary(action => action.Id, action => reloaded.Controller(action, _xboxIdentity, true)), new HashSet<string> { ControlInput.East }));
        Assert.Empty(reloaded.ConflictingSlots(_xboxIdentity));
    }

    /// <summary>
    /// Keeps contextual reuse and longer combinations while detecting equal chords regardless of input order.
    /// </summary>
    [Fact]
    public void ConflictDetectionAllowsContextReuseAndComparesChordSets()
    {
        ControlProfiles profiles = new(() => null, _ => { });
        Assert.Empty(profiles.SetKeyboardSlot(ControlCatalog.Interact, 0, [0x43]));
        Assert.Empty(profiles.SetKeyboardSlot(ControlCatalog.Menu, 0, [0x58]));
        profiles.SetKeyboard(ControlCatalog.Menu, [[0x11, 0x45], [0x1B]]);
        Assert.Empty(profiles.KeyboardConflicts(ControlCatalog.Reset, [0x45]));
        IReadOnlyList<ControlBindingSlot> conflicts = profiles.SetKeyboardSlot(ControlCatalog.Reset, 1, [0x45, 0x11]);
        Assert.Single(conflicts);
        Assert.Equal(ControlCatalog.Menu, conflicts[0].Action);
        Assert.Equal(0, conflicts[0].Slot);
        Assert.Empty(profiles.Keyboard(ControlCatalog.Actions.Single(action => action.Id == ControlCatalog.Menu))[0]);
        Assert.Empty(profiles.SetKeyboardSlot(ControlCatalog.Reset, 1, []));
    }

    /// <summary>
    /// Rolls back both the receiving assignment and the old unbinding when the single preference write fails.
    /// </summary>
    [Fact]
    public void FailedConflictReplacementLeavesEveryOldBindingActive()
    {
        int writes = 0;
        ControlProfiles profiles = new(() => null, _ =>
        {
            writes++;
            throw new IOException("Test preference failure.");
        });
        Assert.Throws<IOException>(() => profiles.SetKeyboardSlot(ControlCatalog.Reset, 1, [0x1B]));
        Assert.Equal(1, writes);
        foreach (ControlAction action in ControlCatalog.Actions)
            Assert.Equal(action.Keyboard, profiles.Keyboard(action));
    }

    /// <summary>
    /// Suppresses ambiguous bindings in older profiles without suppressing valid context sharing or other alternatives.
    /// </summary>
    [Fact]
    public void LegacyExactConflictsCannotFireResetAndMenuTogether()
    {
        ControlProfiles profiles = new(() => null, _ => { });
        profiles.SetKeyboard(ControlCatalog.Reset, [[0x1B]]);
        Assert.Equal(4, profiles.ConflictingSlots(null).Count);
        Dictionary<string, int[][]> bindings = ControlCatalog.Actions.ToDictionary(action => action.Id, profiles.Keyboard);
        Assert.Empty(ControlMatcher.Match(bindings, new HashSet<int> { 0x1B }));
        HashSet<string> alternative = ControlMatcher.Match(bindings, new HashSet<int> { 0x1B, 0x58 });
        Assert.DoesNotContain(ControlCatalog.Reset, alternative);
        Assert.Contains(ControlCatalog.Menu, alternative);
        Assert.Contains(ControlCatalog.Cancel, alternative);
        Assert.Contains(ControlCatalog.BattleRun, alternative);
        Assert.Empty(profiles.SetKeyboardSlot(ControlCatalog.Confirm, 0, [0x43]));
        HashSet<string> contextActions = ControlMatcher.Match(bindings, new HashSet<int> { 0x43 });
        Assert.Contains(ControlCatalog.Confirm, contextActions);
        Assert.Contains(ControlCatalog.Interact, contextActions);
    }

    /// <summary>
    /// Retains different keyboard and controller bindings across a complete reload.
    /// </summary>
    [Fact]
    public void ProfilesRemainIsolatedAcrossDevicesAndReloads()
    {
        string? stored = null;
        ControlProfiles profiles = new(() => stored, value => stored = value);
        profiles.Remember(_xboxIdentity, "Xbox", true);
        profiles.Remember(_otherIdentity, "Other controller", false);
        profiles.SetKeyboard(_confirm, [[0x45]]);
        profiles.SetController(_xboxIdentity, _confirm, [[_start]]);
        profiles.SetController(_otherIdentity, _confirm, [[_rawButton]]);
        ControlProfiles reloaded = new(() => stored, _ => { });
        ControlAction confirm = ControlCatalog.Actions.Single(action => action.Id == _confirm);
        Assert.Equal(0x45, reloaded.Keyboard(confirm)[0][0]);
        Assert.Equal(_start, reloaded.Controller(confirm, _xboxIdentity, true)[0][0]);
        Assert.Equal(_rawButton, reloaded.Controller(confirm, _otherIdentity, false)[0][0]);
        reloaded.Restore(_xboxIdentity);
        Assert.Equal(confirm.Controller[0][0], reloaded.Controller(confirm, _xboxIdentity, true)[0][0]);
        Assert.Equal(_rawButton, reloaded.Controller(confirm, _otherIdentity, false)[0][0]);
        Assert.Equal(0x45, reloaded.Keyboard(confirm)[0][0]);
    }

    /// <summary>
    /// Does not guess an Xbox button layout for a generic joystick.
    /// </summary>
    [Fact]
    public void GenericDeviceStartsUnboundAndClearSurvivesReload()
    {
        string? stored = null;
        ControlProfiles profiles = new(() => stored, value => stored = value);
        profiles.Remember(_otherIdentity, "Generic", false);
        ControlAction reset = ControlCatalog.Actions.Single(action => action.Id == ControlCatalog.Reset);
        Assert.Empty(profiles.Controller(reset, _otherIdentity, false));
        profiles.SetKeyboard(ControlCatalog.Reset, []);
        ControlProfiles reloaded = new(() => stored, _ => { });
        Assert.Empty(reloaded.Keyboard(reset));
        reloaded.Restore(null);
        Assert.Equal(0x76, reloaded.Keyboard(reset)[0][0]);
    }

    /// <summary>
    /// Prevents a controller reset chord from also firing shorter game actions.
    /// </summary>
    [Fact]
    public void LongerChordsConsumeTheirComponentActions()
    {
        Dictionary<string, string[][]> bindings = new()
        {
            [_confirm] = [[_start]],
            [ControlCatalog.Reset] = [[_leftTrigger, _rightTrigger, _start]]
        };

        HashSet<string> actions = ControlMatcher.Match(bindings, new HashSet<string> { _leftTrigger, _rightTrigger, _start });
        Assert.Equal([ControlCatalog.Reset], actions);
        Assert.Equal([_confirm], ControlMatcher.Match(bindings, new HashSet<string> { _start }));
    }

    /// <summary>
    /// Routes each quick-item chord alone and retains defaults and edited alternatives in saved profiles.
    /// </summary>
    /// <param name="id">The quick-item action identifier.</param>
    [Theory]
    [InlineData(ControlCatalog.QuickItemLeft)]
    [InlineData(ControlCatalog.QuickItemRight)]
    [InlineData(ControlCatalog.QuickItemUp)]
    [InlineData(ControlCatalog.QuickItemDown)]
    [InlineData(ControlCatalog.QuickItemUse)]
    public void QuickItemChordsConsumeTheirComponentInputsAndPersist(string id)
    {
        ControlAction action = ControlCatalog.Actions.Single(action => action.Id == id);
        Dictionary<string, string[][]> controller = ControlCatalog.Actions.ToDictionary(action => action.Id, action => action.Controller);
        Dictionary<string, int[][]> keyboard = ControlCatalog.Actions.ToDictionary(action => action.Id, action => action.Keyboard);
        Assert.Equal([id], ControlMatcher.Match(controller, action.Controller[0].ToHashSet()));
        Assert.Equal([id], ControlMatcher.Match(keyboard, action.Keyboard[0].ToHashSet()));
        Assert.Contains(id, ControlCatalog.GameActions);
        Assert.Contains(ControlCatalog.Groups, group => group.Actions.Contains(id));
        string? stored = null;
        ControlProfiles profiles = new(() => stored, value => stored = value);
        profiles.Remember(_xboxIdentity, "Xbox", true);
        ControlProfiles existing = new(() => stored, value => stored = value);
        Assert.Equal(action.Controller, existing.Controller(action, _xboxIdentity, true));
        Assert.Equal(action.Keyboard, existing.Keyboard(action));
        existing.SetKeyboardSlot(id, 1, [0x11, 0x75]);
        existing.SetControllerSlot(_xboxIdentity, id, 1, [ControlInput.Back, ControlInput.North]);
        ControlProfiles reloaded = new(() => stored, _ => { });
        Assert.Equal(action.Keyboard[0], reloaded.Keyboard(action)[0]);
        Assert.Equal([0x11, 0x75], reloaded.Keyboard(action)[1]);
        Assert.Equal(action.Controller[0], reloaded.Controller(action, _xboxIdentity, true)[0]);
        Assert.Equal([ControlInput.Back, ControlInput.North], reloaded.Controller(action, _xboxIdentity, true)[1]);
    }

    /// <summary>
    /// Preserves a field or battle action when a longer chord belongs to an unrelated input context.
    /// </summary>
    [Fact]
    public void ContextualChordsDoNotConsumeUnrelatedFieldOrBattleBindings()
    {
        Dictionary<string, int[][]> bindings = new()
        {
            [ControlCatalog.Confirm] = [[0x11, 0x45]],
            [ControlCatalog.Interact] = [[0x45]],
            [ControlCatalog.Cancel] = [[0x11, 0x58]],
            [ControlCatalog.BattleRun] = [[0x58]]
        };

        HashSet<string> menuAndField = ControlMatcher.Match(bindings, new HashSet<int> { 0x11, 0x45 });
        Assert.Contains(ControlCatalog.Confirm, menuAndField);
        Assert.Contains(ControlCatalog.Interact, menuAndField);
        HashSet<string> cancelAndRun = ControlMatcher.Match(bindings, new HashSet<int> { 0x11, 0x58 });
        Assert.Contains(ControlCatalog.Cancel, cancelAndRun);
        Assert.Contains(ControlCatalog.BattleRun, cancelAndRun);
    }

    /// <summary>
    /// Preserves unrelated actions and any of the configured keyboard alternatives.
    /// </summary>
    [Fact]
    public void KeyboardAlternativesAndNavigationChordsWorkTogether()
    {
        Dictionary<string, int[][]> bindings = ControlCatalog.Actions.ToDictionary(action => action.Id, action => action.Keyboard);
        HashSet<string> actions = ControlMatcher.Match(bindings, new HashSet<int> { 0x11, 0x31, 0x26 });
        Assert.Contains(ControlCatalog.Player, actions);
        Assert.Contains(ControlCatalog.Up, actions);
        Assert.Contains(_confirm, ControlMatcher.Match(bindings, new HashSet<int> { 0x0D }));
    }

    /// <summary>
    /// Uses the exact wire names expected by the Ruby state receiver.
    /// </summary>
    [Fact]
    public void ControlStateUsesTheSharedWireContract()
    {
        ControlStatePayload state = new(true, ControlCatalog.GameActions, [_confirm]);
        var payload = TrackerJson.SerializePayload(state);
        Assert.True(payload.GetProperty(_enabledProperty).GetBoolean());
        Assert.Contains(_confirm, payload.GetProperty(_managedProperty).EnumerateArray().Select(element => element.GetString()));
        Assert.Equal(_confirm, payload.GetProperty(_pressedProperty)[0].GetString());
    }

    /// <summary>
    /// Loads and starts the distributed native SDL controller backend without any physical-device requirement.
    /// </summary>
    [Fact]
    public void DistributedControllerBackendInitializes()
    {
        using SdlControllerSource controllers = new();
        controllers.Start();
        IReadOnlyList<ControllerReading> readings = controllers.Read();
        Assert.NotNull(readings);
        output.WriteLine($"Detected {readings.Count} controller(s): {string.Join(_controllerSeparator, readings.Select(device => device.Name))}");
    }

    /// <summary>
    /// Reads actual SDL virtual joystick changes and removes a detached device.
    /// </summary>
    [Fact]
    public unsafe void GenericJoystickReportsButtonsAxesAndHatsAndDetaches()
    {
        using SdlControllerSource controllers = new();
        controllers.Start();
        HashSet<string> existing = [.. controllers.Read().Select(device => device.Identity)];
        int index = SDL.JoystickAttachVirtual((SDLJoystickType)0, 2, 2, 1);
        Assert.True(index >= 0, SDL.GetErrorS());
        SDLJoystick* joystick = SDL.JoystickOpen(index);
        bool detached = false;
        try
        {
            Assert.True(joystick != null);
            ControllerReading added = Assert.Single(controllers.Read(), device => !existing.Contains(device.Identity));
            Assert.False(added.Mapped);
            Assert.Empty(added.Pressed);
            Assert.Equal(0, SDL.JoystickSetVirtualAxis(joystick, 0, 25_000));
            Assert.Equal(0, SDL.JoystickSetVirtualButton(joystick, 1, 1));
            Assert.Equal(0, SDL.JoystickSetVirtualHat(joystick, 0, 1));
            ControllerReading held = controllers.Read().Single(device => device.Identity == added.Identity);
            Assert.Contains(_rawAxis, held.Pressed);
            Assert.Contains(_rawTestButton, held.Pressed);
            Assert.Contains(_rawHat, held.Pressed);
            Assert.Equal(0, SDL.JoystickDetachVirtual(index));
            detached = true;
            Assert.DoesNotContain(controllers.Read(), device => device.Identity == added.Identity);
        }
        finally
        {
            if (joystick != null)
                SDL.JoystickClose(joystick);

            if (!detached)
                SDL.JoystickDetachVirtual(index);
        }
    }

    /// <summary>
    /// Keeps numpad operators distinct from the main keyboard's punctuation keys.
    /// </summary>
    /// <param name="key">The key text reported by the browser.</param>
    /// <param name="code">The physical key code reported by the browser.</param>
    /// <param name="expected">The expected Windows virtual key.</param>
    [Theory]
    [InlineData(_decimalKey, _decimalCode, 0x6E)]
    [InlineData(_addKey, _addCode, 0x6B)]
    public void KeyboardCapturePreservesNumpadIdentity(string key, string code, int expected)
        => Assert.Equal(expected, ControlNames.EventKey(new KeyboardEventArgs { Key = key, Code = code }));

    /// <summary>
    /// Captures a brief keyboard chord, suppresses navigation during capture, and replaces the old shortcut.
    /// </summary>
    /// <returns>A task that completes when shortcut capture has been verified.</returns>
    [Trait(TrackerTestCategories.Category, TrackerTestCategories.GameCatalog)]
    [Fact]
    public async Task KeyboardCaptureReplacesTrackerWindowShortcuts()
    {
        using TestWorkspace workspace = new();
        TrackerKnowledgeOptions storage = new(workspace.Root);
        TrackerConnectionState state = new();
        await using TrackerConnectionService connection = new(new TrackerConnectionOptions(0, _version, false, TimeSpan.FromSeconds(2)), new TrackerDiagnosticsStore(), state, new TrackerRunState(), new TrackerKnowledgeStore(storage), new AreaDiscoveryStore(storage), new CompletedRunArchive(storage));
        ControlProfiles profiles = new(() => null, _ => { });
        TrackerGlobalShortcutService shortcuts = new(state, connection, profiles);
        KeyboardEventArgs ctrlDown = new() { Key = _controlKey, Code = _controlCode, CtrlKey = true };
        KeyboardEventArgs eDown = new() { Key = _eKey, Code = _eCode, CtrlKey = true };
        KeyboardEventArgs ctrlUp = new() { Key = _controlKey, Code = _controlCode };
        KeyboardEventArgs oldShortcut = new() { Key = _oneKey, Code = _oneCode, CtrlKey = true };
        shortcuts.BeginCapture(ControlCatalog.Player, null);
        Assert.Null(shortcuts.TrackerKeyDown(oldShortcut));
        shortcuts.CaptureKeyDown(ctrlDown);
        shortcuts.CaptureKeyDown(eDown);
        shortcuts.CaptureKeyUp(eDown);
        Assert.NotNull(shortcuts.CaptureAction);
        shortcuts.CaptureKeyUp(ctrlUp);
        Assert.Null(shortcuts.CaptureAction);
        Assert.Null(shortcuts.TrackerKeyDown(oldShortcut));
        shortcuts.ClearTrackerKeys();
        Assert.Equal(TrackerView.Player, shortcuts.TrackerKeyDown(eDown));
        Assert.Equal("Ctrl + E", shortcuts.ShortcutLabel(TrackerView.Player));
    }

    /// <summary>
    /// Keeps previous bindings active when preference persistence fails.
    /// </summary>
    [Fact]
    public void FailedWriteDoesNotApplyUnsavedBindings()
    {
        ControlProfiles profiles = new(() => null, _ => throw new IOException("Test preference failure."));
        ControlAction reset = ControlCatalog.Actions.Single(action => action.Id == ControlCatalog.Reset);
        Assert.Throws<IOException>(() => profiles.SetKeyboard(ControlCatalog.Reset, [[0x45]]));
        Assert.Equal(0x76, profiles.Keyboard(reset)[0][0]);
    }

    /// <summary>
    /// Recovers from malformed persisted collection values without disabling input.
    /// </summary>
    [Fact]
    public void CorruptProfileCollectionsFallBackToDefaults()
    {
        ControlProfiles profiles = new(() => _corruptCollections, _ => { });
        Assert.Empty(profiles.Controllers);
        ControlAction confirm = ControlCatalog.Actions.Single(action => action.Id == _confirm);
        Assert.Equal(confirm.Keyboard, profiles.Keyboard(confirm));
    }

    /// <summary>
    /// Preserves empty positional slots and independent keyboard and device edits after reload.
    /// </summary>
    [Fact]
    public void BindingSlotsRemainIndependentAcrossReloads()
    {
        string? stored = null;
        ControlProfiles profiles = new(() => stored, value => stored = value);
        profiles.Remember(_xboxIdentity, "Xbox", true);
        profiles.Remember(_otherIdentity, "DualSense", true);
        ControlAction up = ControlCatalog.Actions.Single(action => action.Id == ControlCatalog.Up);
        profiles.SetKeyboardSlot(up.Id, 1, [0x45]);
        profiles.SetKeyboardSlot(up.Id, 0, []);
        profiles.SetControllerSlot(_xboxIdentity, up.Id, 1, [_start]);
        profiles.SetControllerSlot(_xboxIdentity, up.Id, 0, []);
        ControlProfiles reloaded = new(() => stored, _ => { });
        Assert.Empty(reloaded.Keyboard(up)[0]);
        Assert.Equal([0x45], reloaded.Keyboard(up)[1]);
        Assert.Empty(reloaded.Controller(up, _xboxIdentity, true)[0]);
        Assert.Equal([_start], reloaded.Controller(up, _xboxIdentity, true)[1]);
        Assert.Equal(up.Controller, reloaded.Controller(up, _otherIdentity, true));
        Assert.Empty(ControlMatcher.Match(new Dictionary<string, int[][]> { [up.Id] = reloaded.Keyboard(up) }, new HashSet<int>()));
        Assert.Equal([up.Id], ControlMatcher.Match(new Dictionary<string, int[][]> { [up.Id] = reloaded.Keyboard(up) }, new HashSet<int> { 0x45 }));
    }

    /// <summary>
    /// Migrates combined controls once and allows all new contextual actions to diverge thereafter.
    /// </summary>
    [Fact]
    public void LegacyCombinedBindingsMigrateWithoutLinkingFutureEdits()
    {
        string? stored = null;
        ControlProfiles legacy = new(() => null, value => stored = value);
        legacy.SetKeyboard(ControlCatalog.Confirm, [[0x45], [0x44], [0x47]]);
        legacy.SetKeyboard(ControlCatalog.Cancel, [[0x46]]);
        ControlProfileDocument document = System.Text.Json.JsonSerializer.Deserialize<ControlProfileDocument>(stored!)!;
        document.SchemaVersion = 0;
        stored = System.Text.Json.JsonSerializer.Serialize(document);
        ControlProfiles migrated = new(() => stored, value => stored = value);
        ControlAction interact = ControlCatalog.Actions.Single(action => action.Id == ControlCatalog.Interact);
        ControlAction menu = ControlCatalog.Actions.Single(action => action.Id == ControlCatalog.Menu);
        ControlAction run = ControlCatalog.Actions.Single(action => action.Id == ControlCatalog.BattleRun);
        Assert.Equal([0x45], migrated.Keyboard(interact)[0]);
        Assert.Equal(2, migrated.Keyboard(interact).Length);
        Assert.Empty(ControlMatcher.Match(new Dictionary<string, int[][]> { [interact.Id] = migrated.Keyboard(interact) }, new HashSet<int> { 0x47 }));
        Assert.Equal([0x46], migrated.Keyboard(menu)[0]);
        Assert.Equal([0x46], migrated.Keyboard(run)[0]);
        migrated.SetKeyboardSlot(ControlCatalog.Confirm, 0, [0x47]);
        migrated.SetKeyboardSlot(ControlCatalog.Cancel, 0, [0x48]);
        ControlProfiles reloaded = new(() => stored, _ => { });
        Assert.Equal([0x45], reloaded.Keyboard(interact)[0]);
        Assert.Equal([0x46], reloaded.Keyboard(menu)[0]);
        Assert.Equal([0x46], reloaded.Keyboard(run)[0]);
        reloaded.Restore(null);
        Assert.Equal(interact.Keyboard, reloaded.Keyboard(interact));
    }

    /// <summary>
    /// Leaves a preview unsaved until confirmed and modifies only the requested second slot.
    /// </summary>
    /// <returns>A task that completes after the capture session and transport are disposed.</returns>
    [Trait(TrackerTestCategories.Category, TrackerTestCategories.GameCatalog)]
    [Fact]
    public async Task CapturePreviewCanBeCancelledOrCommittedToTheSecondSlot()
    {
        using TestWorkspace workspace = new();
        TrackerKnowledgeOptions storage = new(workspace.Root);
        TrackerConnectionState state = new();
        await using TrackerConnectionService connection = new(new TrackerConnectionOptions(0, _version, false, TimeSpan.FromSeconds(2)), new TrackerDiagnosticsStore(), state, new TrackerRunState(), new TrackerKnowledgeStore(storage), new AreaDiscoveryStore(storage), new CompletedRunArchive(storage));
        ControlProfiles profiles = new(() => null, _ => { });
        profiles.Remember(_otherIdentity, "DualSense", true);
        TrackerGlobalShortcutService shortcuts = new(state, connection, profiles);
        ControlAction confirm = ControlCatalog.Actions.Single(action => action.Id == ControlCatalog.Confirm);
        KeyboardEventArgs input = new() { Key = _eKey, Code = _eCode };
        shortcuts.BeginCapture(confirm.Id, null, 1, confirm: true);
        shortcuts.CaptureKeyDown(input);
        shortcuts.CaptureKeyUp(input);
        Assert.True(shortcuts.CaptureReady);
        Assert.Equal([0x0D], profiles.Keyboard(confirm)[1]);
        shortcuts.CancelCapture();
        Assert.False(shortcuts.CommitCapture());
        Assert.Equal([0x0D], profiles.Keyboard(confirm)[1]);
        shortcuts.BeginCapture(confirm.Id, null, 1, confirm: true);
        shortcuts.CaptureKeyDown(input);
        shortcuts.CaptureKeyUp(input);
        Assert.True(shortcuts.CommitCapture());
        Assert.Equal([0x43], profiles.Keyboard(confirm)[0]);
        Assert.Equal([0x45], profiles.Keyboard(confirm)[1]);
        shortcuts.BeginCapture(confirm.Id, _otherIdentity, 1, confirm: true);
        shortcuts.PreviewController([ControlInput.LeftTrigger, ControlInput.RightTrigger]);
        Assert.True(shortcuts.CommitCapture());
        Assert.Equal([ControlInput.South], profiles.Controller(confirm, _otherIdentity, true)[0]);
        Assert.Equal([ControlInput.LeftTrigger, ControlInput.RightTrigger], profiles.Controller(confirm, _otherIdentity, true)[1]);
        System.Reflection.MethodInfo capture = typeof(TrackerGlobalShortcutService).GetMethod(_captureMethod, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        shortcuts.BeginCapture(confirm.Id, _otherIdentity, 1, confirm: true);
        capture.Invoke(shortcuts, [(IReadOnlyList<ControllerReading>)[new(_otherIdentity, "DualSense", true, new HashSet<string> { ControlInput.South })]]);
        capture.Invoke(shortcuts, [(IReadOnlyList<ControllerReading>)[new(_otherIdentity, "DualSense", true, new HashSet<string>())]]);
        capture.Invoke(shortcuts, [(IReadOnlyList<ControllerReading>)[new(_otherIdentity, "DualSense", true, new HashSet<string> { ControlInput.Start })]]);
        Assert.False(shortcuts.CaptureReady);
        capture.Invoke(shortcuts, [(IReadOnlyList<ControllerReading>)[new(_otherIdentity, "DualSense", true, new HashSet<string>())]]);
        Assert.True(shortcuts.CaptureReady);
        Assert.Equal([ControlInput.Start], shortcuts.CapturedController);
        Assert.Equal([ControlInput.LeftTrigger, ControlInput.RightTrigger], profiles.Controller(confirm, _otherIdentity, true)[1]);
        Assert.True(shortcuts.CommitCapture());
        Assert.Equal([ControlInput.South], profiles.Controller(confirm, _otherIdentity, true)[0]);
        Assert.Equal([ControlInput.Start], profiles.Controller(confirm, _otherIdentity, true)[1]);
        shortcuts.BeginCapture(confirm.Id, null, 1, confirm: true);
        KeyboardEventArgs alt = new() { Key = _altKey, Code = _altCode, AltKey = true };
        KeyboardEventArgs enter = new() { Key = _enterKey, Code = _enterKey, AltKey = true };
        shortcuts.CaptureKeyDown(alt);
        shortcuts.CaptureKeyDown(enter);
        shortcuts.CaptureKeyUp(enter);
        shortcuts.CaptureKeyUp(new KeyboardEventArgs { Key = _altKey, Code = _altCode });
        Assert.False(shortcuts.CaptureReady);
        Assert.False(shortcuts.CommitCapture());
        Assert.NotNull(shortcuts.BindingError);
        Assert.Equal([0x45], profiles.Keyboard(confirm)[1]);
        shortcuts.CancelCapture();
    }
}
