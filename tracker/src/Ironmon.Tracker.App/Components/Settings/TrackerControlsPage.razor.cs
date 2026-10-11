using Ironmon.Tracker.App.Controls;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Ironmon.Tracker.App.Components.Settings;

/// <summary>
/// Edits two independent binding slots in categorized keyboard and controller profiles.
/// </summary>
public partial class TrackerControlsPage
{
    private const string _profileControlId = "settings-control-profile";
    private const string _keyboardProfile = "keyboard";
    private const string _actionsPanelId = "settings-control-actions";
    private const string _categoryIdPrefix = "settings-control-category-";
    private const string _controlsSuffix = " controls";
    private const string _chordSeparator = " + ";
    private const string _conflictSeparator = "; ";
    private const string _arrowLeftKey = "ArrowLeft";
    private const string _arrowRightKey = "ArrowRight";
    private const string _arrowUpKey = "ArrowUp";
    private const string _arrowDownKey = "ArrowDown";
    private const string _homeKey = "Home";
    private const string _endKey = "End";
    private const string _profileHelp = "Tracker bindings take priority while the tracker is connected and the game is active. Closing the tracker restores the game's own controls. Select a connected controller to edit its bindings. F1, F12, and Alt+Enter remain reserved by the game.";
    private string? _controllerIdentity;
    private string? _error;
    private int _selectedGroup = 1;
    private int _captureSlot;
    private ElementReference _captureRoot;
    private ElementReference _useBindingButton;
    private readonly ElementReference[] _categoryButtons = new ElementReference[ControlCatalog.Groups.Count];
    private bool _focusCapturePending;
    private bool _focusCategoryPending;
    private bool _focusUsePending;
    private bool _previewFocused;

    /// <summary>
    /// Gets the persisted input profiles.
    /// </summary>
    [Inject]
    private ControlProfiles Profiles { get; set; } = null!;

    /// <summary>
    /// Gets the input monitor and binding capture service.
    /// </summary>
    [Inject]
    private TrackerGlobalShortcutService Shortcuts { get; set; } = null!;

    /// <summary>
    /// Gets the connected game's control protocol support.
    /// </summary>
    [Inject]
    private TrackerConnectionState ConnectionState { get; set; } = null!;

    /// <summary>
    /// Gets the selected saved controller profile.
    /// </summary>
    private ControllerProfile? SelectedController => Profiles.Controllers.FirstOrDefault(controller => controller.Identity == _controllerIdentity);

    /// <summary>
    /// Gets whether the selected controller uses PlayStation labels.
    /// </summary>
    private bool IsPlayStation => ControlNames.IsPlayStation(SelectedController?.Name);

    /// <summary>
    /// Gets the selected category.
    /// </summary>
    private ControlGroup SelectedGroup => ControlCatalog.Groups[_selectedGroup];

    /// <summary>
    /// Gets a warning for ambiguous bindings saved by an older tracker version.
    /// </summary>
    private string? ExistingConflictWarning
    {
        get
        {
            IReadOnlyList<ControlBindingSlot> conflicts = Profiles.ConflictingSlots(_controllerIdentity);
            if (conflicts.Count == 0)
                return null;

            string bindings = string.Join(_conflictSeparator, conflicts.Select(binding => $"{ControlCatalog.Actions.Single(action => action.Id == binding.Action).Label} (Binding {binding.Slot + 1})"));
            return $"Conflicting bindings are inactive. Reassign a binding to resolve: {bindings}.";
        }
    }

    /// <summary>
    /// Gets the selected category's ordered actions.
    /// </summary>
    private IEnumerable<ControlAction> SelectedActions => SelectedGroup.Actions.Select(id => ControlCatalog.Actions.Single(action => action.Id == id));

    /// <summary>
    /// Gets the profile's current connection label.
    /// </summary>
    private string ProfileStatus => _controllerIdentity is null ? "Keyboard" : IsConnected(_controllerIdentity) ? "Connected" : "Disconnected";

    /// <summary>
    /// Gets the binding dialog's selected profile and slot.
    /// </summary>
    private string CaptureContext => $"{SelectedController?.Name ?? "Keyboard"} · Binding {_captureSlot + 1}";

    /// <summary>
    /// Gets the full accessible name for the captured chord.
    /// </summary>
    private string CapturedLabel => _controllerIdentity is null ? string.Join(_chordSeparator, Shortcuts.CapturedKeyboard.Select(ControlNames.Keyboard)) : string.Join(_chordSeparator, Shortcuts.CapturedController.Select(token => ControlNames.Controller(token, IsPlayStation)));

    /// <summary>
    /// Subscribes to hardware and binding changes.
    /// </summary>
    protected override void OnInitialized()
    {
        Shortcuts.ControlsChanged += HandleControlsChanged;
        ConnectionState.Changed += HandleConnectionChanged;
    }

    /// <summary>
    /// Focuses a newly opened capture dialog or a category selected with the keyboard.
    /// </summary>
    /// <param name="firstRender">Whether this is the component's first render.</param>
    /// <returns>A task that completes when any requested focus has been applied.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusCapturePending)
        {
            _focusCapturePending = false;
            await _captureRoot.FocusAsync(preventScroll: true);
        }

        if (_focusCategoryPending)
        {
            _focusCategoryPending = false;
            await _categoryButtons[_selectedGroup].FocusAsync(preventScroll: true);
        }

        if (_focusUsePending && Shortcuts.CaptureReady)
        {
            _focusUsePending = false;
            await _useBindingButton.FocusAsync(preventScroll: true);
        }
    }

    /// <summary>
    /// Opens the capture dialog for one binding slot.
    /// </summary>
    /// <param name="action">The action identifier.</param>
    /// <param name="slot">The zero-based binding slot.</param>
    private void BeginCapture(string action, int slot)
    {
        _captureSlot = slot;
        _error = null;
        _previewFocused = false;
        Shortcuts.BeginCapture(action, _controllerIdentity, slot, confirm: true);
        _focusCapturePending = true;
    }

    /// <summary>
    /// Toggles one mapped controller input in the chord preview.
    /// </summary>
    /// <param name="token">The standard controller input.</param>
    private void ToggleControllerInput(string token)
    {
        HashSet<string> chord = [.. Shortcuts.CapturedController];
        if (!chord.Remove(token))
            chord.Add(token);

        Shortcuts.PreviewController([.. chord]);
    }

    /// <summary>
    /// Saves the released chord preview.
    /// </summary>
    private void UseCapture() => Shortcuts.CommitCapture();

    /// <summary>
    /// Clears only the edited slot and closes the dialog after a successful save.
    /// </summary>
    private void ClearCapture()
    {
        if (Shortcuts.CaptureAction is not string action)
            return;

        Apply(() =>
        {
            if (_controllerIdentity is null)
            {
                Profiles.SetKeyboardSlot(action, _captureSlot, []);
            }
            else
            {
                Profiles.SetControllerSlot(_controllerIdentity, action, _captureSlot, []);
            }

            Shortcuts.CancelCapture();
        });
    }

    /// <summary>
    /// Selects the keyboard or a saved controller profile.
    /// </summary>
    /// <param name="args">The profile selector's change event.</param>
    private void SelectProfile(ChangeEventArgs args)
    {
        Shortcuts.CancelCapture();
        string? value = args.Value?.ToString();
        _controllerIdentity = value == _keyboardProfile ? null : value;
        _error = null;
        Shortcuts.NotifyBindingsChanged();
    }

    /// <summary>
    /// Selects one control category.
    /// </summary>
    /// <param name="category">The category index.</param>
    private void SelectCategory(int category)
        => _selectedGroup = category;

    /// <summary>
    /// Supports arrow, Home, and End navigation between category tabs.
    /// </summary>
    /// <param name="args">The category button's key event.</param>
    /// <param name="category">The currently focused category.</param>
    private void HandleCategoryKey(KeyboardEventArgs args, int category)
    {
        int target = args.Key switch
        {
            _arrowLeftKey => category - 1,
            _arrowRightKey => category + 1,
            _arrowUpKey => category - 3,
            _arrowDownKey => category + 3,
            _homeKey => 0,
            _endKey => ControlCatalog.Groups.Count - 1,
            _ => category
        };

        if (target == category)
            return;

        _selectedGroup = (target + ControlCatalog.Groups.Count) % ControlCatalog.Groups.Count;
        _focusCategoryPending = true;
    }

    /// <summary>
    /// Gets the accessible identifier for one category tab.
    /// </summary>
    /// <param name="category">The category index.</param>
    /// <returns>The tab's DOM identifier.</returns>
    private static string CategoryId(int category)
        => _categoryIdPrefix + category;

    /// <summary>
    /// Determines whether a saved controller is attached.
    /// </summary>
    /// <param name="identity">The controller's persistent identity.</param>
    /// <returns>Whether the controller appears in the device inventory.</returns>
    private bool IsConnected(string identity)
        => Shortcuts.Controllers.Any(controller => controller.Identity == identity);

    /// <summary>
    /// Gets one positional keyboard chord for the selected profile.
    /// </summary>
    /// <param name="action">The action to display.</param>
    /// <param name="slot">The binding slot.</param>
    /// <returns>The chord, or an empty array for an unbound or controller slot.</returns>
    private int[] KeyboardChord(ControlAction action, int slot)
        => _controllerIdentity is null ? Profiles.Keyboard(action).ElementAtOrDefault(slot) ?? [] : [];

    /// <summary>
    /// Gets one positional controller chord for the selected profile.
    /// </summary>
    /// <param name="action">The action to display.</param>
    /// <param name="slot">The binding slot.</param>
    /// <returns>The chord, or an empty array for an unbound or keyboard slot.</returns>
    private string[] ControllerChord(ControlAction action, int slot)
        => _controllerIdentity is not null ? Profiles.Controller(action, _controllerIdentity, SelectedController?.Mapped == true).ElementAtOrDefault(slot) ?? [] : [];

    /// <summary>
    /// Describes a binding button and any truncated chord to assistive technology and mouse users.
    /// </summary>
    /// <param name="action">The action to edit.</param>
    /// <param name="slot">The binding slot.</param>
    /// <returns>The action, slot, and full chord.</returns>
    private string BindingAccessibleLabel(ControlAction action, int slot)
    {
        string label = _controllerIdentity is null ? string.Join(_chordSeparator, KeyboardChord(action, slot).Select(ControlNames.Keyboard)) : string.Join(_chordSeparator, ControllerChord(action, slot).Select(token => ControlNames.Controller(token, IsPlayStation)));
        return $"{action.Label}, binding {slot + 1}: {(label.Length == 0 ? "Unbound" : label)}";
    }

    /// <summary>
    /// Restores only the selected profile.
    /// </summary>
    private void RestoreDefaults()
        => Apply(() => Profiles.Restore(_controllerIdentity));

    /// <summary>
    /// Applies a profile edit and reports any persistence failure.
    /// </summary>
    /// <param name="edit">The edit to persist.</param>
    private void Apply(Action edit)
    {
        try
        {
            edit();
            _error = null;
            Shortcuts.NotifyBindingsChanged();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _error = "The binding could not be saved. Check access to tracker preferences.";
        }
    }

    /// <summary>
    /// Refreshes the profile editor after a connection change.
    /// </summary>
    /// <param name="sender">The state raising the event.</param>
    /// <param name="args">The connection change event.</param>
    private void HandleConnectionChanged(object? sender, EventArgs args)
        => HandleControlsChanged();

    /// <summary>
    /// Refreshes the editor on the renderer's dispatcher.
    /// </summary>
    private void HandleControlsChanged()
    {
        _ = InvokeAsync(() =>
        {
            if (!Shortcuts.CaptureReady)
            {
                _previewFocused = false;
                _focusUsePending = false;
            }
            else if (_controllerIdentity is null && !_previewFocused)
            {
                _previewFocused = true;
                _focusUsePending = true;
            }

            StateHasChanged();
        });
    }

    /// <summary>
    /// Stops capture and removes subscriptions when the controls tab closes.
    /// </summary>
    public void Dispose()
    {
        Shortcuts.ControlsChanged -= HandleControlsChanged;
        ConnectionState.Changed -= HandleConnectionChanged;
        Shortcuts.CancelCapture();
    }
}
