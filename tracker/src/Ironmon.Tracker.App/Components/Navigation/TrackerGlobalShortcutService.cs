using System.Diagnostics;
using System.Runtime.InteropServices;
using Ironmon.Tracker.App.Controls;
using Microsoft.AspNetCore.Components.Web;

namespace Ironmon.Tracker.App.Components.Navigation;

/// <summary>
/// Owns foreground-safe input, binding capture, and temporary game control overrides.
/// </summary>
public sealed partial class TrackerGlobalShortcutService
{
    private const int _pollIntervalMilliseconds = 16;
    private const int _pressedKeyMask = 0x8000;
    private const int _keepAliveMilliseconds = 250;
    private const int _writeTimeoutMilliseconds = 250;
    private const string _userLibrary = "user32.dll";
    private const string _chordSeparator = " + ";
    private const string _alternativeSeparator = " / ";
    private const string _conflictSeparator = "; ";
    private readonly TrackerConnectionState _connectionState;
    private readonly TrackerConnectionService _connectionService;
    private readonly ControlProfiles _profiles;
    private readonly Lock _sync = new();
    private CancellationTokenSource? _cancellation;
    private Task? _pollTask;
    private IReadOnlyList<ControllerReading> _controllers = [];
    private HashSet<string> _previousActions = [];
    private HashSet<string> _lastGameActions = [];
    private long _lastWrite;
    private bool _overrideEnabled;
    private string? _captureAction;
    private string? _captureController;
    private bool _captureArmed;
    private int _captureSlot;
    private bool _confirmCapture;
    private bool _captureReady;
    private readonly HashSet<int> _capturedKeys = [];
    private readonly HashSet<string> _capturedControls = [];
    private readonly HashSet<int> _browserKeys = [];
    private readonly HashSet<int> _captureBrowserKeys = [];

    /// <summary>
    /// Initializes the input worker and profile store.
    /// </summary>
    /// <param name="connectionState">The connected game's current state.</param>
    /// <param name="connectionService">The tracker transport and request service.</param>
    /// <param name="profiles">The persisted keyboard and controller profiles.</param>
    public TrackerGlobalShortcutService(TrackerConnectionState connectionState, TrackerConnectionService connectionService, ControlProfiles profiles)
    {
        _connectionState = connectionState;
        _connectionService = connectionService;
        _profiles = profiles;
    }

    /// <summary>
    /// Occurs when a configured shortcut requests a tracker view.
    /// </summary>
    public event Action<TrackerView>? ViewRequested;

    /// <summary>
    /// Occurs when a controller requests the next active opponent.
    /// </summary>
    public event Action? EnemyCycleRequested;

    /// <summary>
    /// Occurs when device inventory or binding capture changes.
    /// </summary>
    public event Action? ControlsChanged;

    /// <summary>
    /// Gets currently connected controllers and their held controls.
    /// </summary>
    public IReadOnlyList<ControllerReading> Controllers
    {
        get
        {
            lock (_sync)
                return _controllers;
        }
    }

    /// <summary>
    /// Gets the action currently waiting for a released input chord.
    /// </summary>
    public string? CaptureAction
    {
        get
        {
            lock (_sync)
                return _captureAction;
        }
    }

    /// <summary>
    /// Gets any failure to initialize controller input.
    /// </summary>
    public string? ControllerError { get; private set; }

    /// <summary>
    /// Gets a failure to save a captured controller or keyboard binding.
    /// </summary>
    public string? BindingError { get; private set; }

    /// <summary>
    /// Gets the old bindings unbound by the latest successful assignment.
    /// </summary>
    public string? BindingWarning { get; private set; }

    /// <summary>
    /// Gets the warning for old bindings that the current preview would replace.
    /// </summary>
    public string? CaptureWarning
    {
        get
        {
            lock (_sync)
            {
                if (_captureAction is null || !_captureReady)
                    return null;

                IReadOnlyList<ControlBindingSlot> conflicts = _captureController is null
                    ? _profiles.KeyboardConflicts(_captureAction, [.. _capturedKeys])
                    : _profiles.ControllerConflicts(_captureController, _captureAction, [.. _capturedControls]);

                return conflicts.Count == 0 ? null : $"Using this binding will unbind: {DescribeConflicts(conflicts)}.";
            }
        }
    }

    /// <summary>
    /// Gets whether a released chord is ready for confirmation.
    /// </summary>
    public bool CaptureReady
    {
        get
        {
            lock (_sync)
                return _captureReady;
        }
    }

    /// <summary>
    /// Gets the recorded keyboard chord for the binding preview.
    /// </summary>
    public int[] CapturedKeyboard
    {
        get
        {
            lock (_sync)
                return [.. _capturedKeys.Order()];
        }
    }

    /// <summary>
    /// Gets the recorded controller chord for the binding preview.
    /// </summary>
    public string[] CapturedController
    {
        get
        {
            lock (_sync)
                return [.. _capturedControls.Order(StringComparer.Ordinal)];
        }
    }

    /// <summary>
    /// Starts a dedicated SDL input thread.
    /// </summary>
    public void Start()
    {
        lock (_sync)
        {
            if (_pollTask is not null)
                return;

            _cancellation = new();
            CancellationToken token = _cancellation.Token;
            _pollTask = Task.Factory.StartNew(() => PollLoop(token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Stops monitoring and releases the game's temporary override.
    /// </summary>
    /// <returns>A task that completes after the input worker releases its resources.</returns>
    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? task;
        lock (_sync)
        {
            cancellation = _cancellation;
            task = _pollTask;
            _cancellation = null;
            _pollTask = null;
        }

        if (cancellation is null || task is null)
            return;

        cancellation.Cancel();
        await task.ConfigureAwait(false);
        cancellation.Dispose();
    }

    /// <summary>
    /// Begins capture after the editor's initiating controls are released.
    /// </summary>
    /// <param name="action">The action identifier to bind.</param>
    /// <param name="controller">The controller identity, or null for keyboard capture.</param>
    /// <param name="slot">The zero-based binding slot to edit.</param>
    /// <param name="confirm">Whether to preview the released chord before saving it.</param>
    public void BeginCapture(string action, string? controller, int slot = 0, bool confirm = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 1);
        lock (_sync)
        {
            _captureAction = action;
            _captureController = controller;
            _captureSlot = slot;
            _confirmCapture = confirm;
            _captureReady = false;
            BindingError = null;
            BindingWarning = null;
            _captureArmed = false;
            _capturedKeys.Clear();
            _capturedControls.Clear();
            _captureBrowserKeys.Clear();
        }

        ControlsChanged?.Invoke();
    }

    /// <summary>
    /// Cancels capture without replacing any saved binding.
    /// </summary>
    public void CancelCapture()
    {
        lock (_sync)
        {
            _captureAction = null;
            _captureReady = false;
        }

        ControlsChanged?.Invoke();
    }

    /// <summary>
    /// Refreshes shortcut labels after a profile edit outside capture.
    /// </summary>
    public void NotifyBindingsChanged()
    {
        BindingWarning = null;
        ControlsChanged?.Invoke();
    }

    /// <summary>
    /// Previews mapped controller inputs selected with the binding dialog's buttons.
    /// </summary>
    /// <param name="chord">The selected standard controller inputs.</param>
    public void PreviewController(IReadOnlyList<string> chord)
    {
        if (chord.Any(token => !ControlCatalog.ControllerInputs.Contains(token)))
            throw new ArgumentException("The controller preview contains an unsupported input.", nameof(chord));

        lock (_sync)
        {
            if (_captureAction is null || _captureController is null)
                return;

            _capturedControls.Clear();
            _capturedControls.UnionWith(chord);
            _captureReady = _capturedControls.Count > 0;
        }

        ControlsChanged?.Invoke();
    }

    /// <summary>
    /// Saves the previewed chord into its selected slot.
    /// </summary>
    /// <returns>Whether the binding was saved successfully.</returns>
    public bool CommitCapture()
    {
        bool saved = false;
        lock (_sync)
        {
            if (_captureAction is null || !_captureReady)
                return false;

            try
            {
                SaveCapture();
                saved = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                BindingError = "The binding could not be saved. Check access to tracker preferences.";
            }
        }

        ControlsChanged?.Invoke();
        return saved;
    }

    /// <summary>
    /// Saves the captured slot and reports old conflicting bindings unbound in the selected profile.
    /// </summary>
    private void SaveCapture()
    {
        IReadOnlyList<ControlBindingSlot> conflicts = _captureController is null
            ? _profiles.SetKeyboardSlot(_captureAction!, _captureSlot, [.. _capturedKeys.Order()])
            : _profiles.SetControllerSlot(_captureController, _captureAction!, _captureSlot, [.. _capturedControls.Order(StringComparer.Ordinal)]);

        BindingError = null;
        BindingWarning = conflicts.Count == 0 ? null : $"Unbound: {DescribeConflicts(conflicts)}. The new binding was saved.";
        _captureAction = null;
        _captureReady = false;
    }

    /// <summary>
    /// Describes the affected actions and positional slots in a binding warning.
    /// </summary>
    /// <param name="conflicts">The old slots affected by an assignment.</param>
    /// <returns>The action labels and one-based binding numbers.</returns>
    private static string DescribeConflicts(IReadOnlyList<ControlBindingSlot> conflicts)
        => string.Join(_conflictSeparator, conflicts.Select(binding => $"{ControlCatalog.Actions.Single(action => action.Id == binding.Action).Label} (Binding {binding.Slot + 1})"));

    /// <summary>
    /// Captures keyboard events delivered by the editor, including taps shorter than a polling interval.
    /// </summary>
    /// <param name="args">The key-down event delivered to the binding editor.</param>
    public void CaptureKeyDown(KeyboardEventArgs args)
    {
        lock (_sync)
        {
            if (_captureAction is null || _captureController is not null || ControlNames.EventKey(args) is not int key)
                return;

            if (_captureReady)
            {
                _capturedKeys.Clear();
                _captureReady = false;
            }

            _captureBrowserKeys.Add(key);
            _captureBrowserKeys.UnionWith(ControlNames.EventModifiers(args));
            _capturedKeys.UnionWith(_captureBrowserKeys);
        }
    }

    /// <summary>
    /// Completes the recorded keyboard chord once every component has been released.
    /// </summary>
    /// <param name="args">The key-up event delivered to the binding editor.</param>
    public void CaptureKeyUp(KeyboardEventArgs args)
    {
        bool completed = false;
        lock (_sync)
        {
            if (_captureAction is null || _captureController is not null)
                return;

            if (ControlNames.EventKey(args) is int key)
                _captureBrowserKeys.Remove(key);

            _captureBrowserKeys.RemoveWhere(key => key is 0x10 or 0x11 or 0x12 && !ControlNames.EventModifiers(args).Contains(key));
            if (_captureBrowserKeys.Count != 0 || _capturedKeys.Count == 0)
                return;

            try
            {
                if (_capturedKeys.Contains(0x12) && _capturedKeys.Contains(0x0D))
                {
                    _capturedKeys.Clear();
                    _captureReady = false;
                    BindingError = "Alt+Enter is reserved for the game's fullscreen control. Choose another binding.";
                }
                else
                {
                    _captureReady = true;
                    if (!_confirmCapture)
                        SaveCapture();

                    BindingError = null;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                BindingError = "The binding could not be saved. Check access to tracker preferences.";
                _captureAction = null;
            }

            completed = true;
        }

        if (completed)
            ControlsChanged?.Invoke();
    }

    /// <summary>
    /// Matches the configured tracker shortcuts while its own window receives keyboard events.
    /// </summary>
    /// <param name="args">The key-down event delivered to the tracker shell.</param>
    /// <returns>The requested view, or null when no navigation shortcut matches.</returns>
    public TrackerView? TrackerKeyDown(KeyboardEventArgs args)
    {
        lock (_sync)
        {
            if (_captureAction is not null || args.Repeat || ControlNames.EventKey(args) is not int key)
                return null;

            _browserKeys.Add(key);
            _browserKeys.UnionWith(ControlNames.EventModifiers(args));
            HashSet<string> actions = ControlMatcher.Match(ControlCatalog.Actions.ToDictionary(action => action.Id, _profiles.Keyboard), _browserKeys);
            return ResolveView(actions.FirstOrDefault(ControlCatalog.IsTrackerAction));
        }
    }

    /// <summary>
    /// Releases browser keyboard state without emitting a shortcut.
    /// </summary>
    /// <param name="args">The key-up event delivered to the tracker shell.</param>
    public void TrackerKeyUp(KeyboardEventArgs args)
    {
        lock (_sync)
        {
            if (ControlNames.EventKey(args) is int key)
                _browserKeys.Remove(key);

            _browserKeys.RemoveWhere(key => key is 0x10 or 0x11 or 0x12 && !ControlNames.EventModifiers(args).Contains(key));
        }
    }

    /// <summary>
    /// Clears browser key latches when focus moves to an editor or another window.
    /// </summary>
    public void ClearTrackerKeys()
    {
        lock (_sync)
            _browserKeys.Clear();
    }

    /// <summary>
    /// Gets the current keyboard alternatives for one tracker tab's tooltip.
    /// </summary>
    /// <param name="view">The tracker view whose shortcut is displayed.</param>
    /// <returns>The readable keyboard alternatives, or an empty string for an unknown view.</returns>
    public string ShortcutLabel(TrackerView view)
    {
        ControlAction? action = ControlCatalog.Actions.FirstOrDefault(action => ResolveView(action.Id) == view);
        return action is null ? string.Empty : string.Join(_alternativeSeparator, _profiles.Keyboard(action).Where(chord => chord.Length > 0).Select(chord => string.Join(_chordSeparator, chord.Select(ControlNames.Keyboard))));
    }

    /// <summary>
    /// Maps tracker action identifiers to their views.
    /// </summary>
    /// <param name="action">The action identifier to resolve.</param>
    /// <returns>The matching tracker view, or null for another action.</returns>
    private static TrackerView? ResolveView(string? action) => action switch
    {
        ControlCatalog.Player => TrackerView.Player,
        ControlCatalog.Enemy => TrackerView.Enemy,
        ControlCatalog.Lookup => TrackerView.Lookup,
        ControlCatalog.Archive => TrackerView.Archive,
        _ => null
    };

    /// <summary>
    /// Polls input on the SDL-owning thread with bounded network writes.
    /// </summary>
    /// <param name="token">The cancellation token that stops input monitoring.</param>
    private void PollLoop(CancellationToken token)
    {
        using SdlControllerSource source = new();
        bool controllerAvailable = true;
        try
        {
            source.Start();
        }
        catch (Exception exception) when (exception is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException or TypeInitializationException)
        {
            controllerAvailable = false;
            ControllerError = "Controller input could not start. Keyboard controls remain available.";
            ControlsChanged?.Invoke();
        }

        try
        {
            while (!token.IsCancellationRequested)
            {
                IReadOnlyList<ControllerReading> controllers = controllerAvailable ? source.Read() : [];
                try
                {
                    PublishControllers(controllers);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    ReportBindingError();
                }

                HashSet<int> keyboard = ReadKeyboard();
                bool capturing;
                lock (_sync)
                    capturing = _captureAction is not null;

                if (capturing && IsTrackerForeground())
                {
                    try
                    {
                        Capture(controllers);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        CancelCapture();
                        ReportBindingError();
                    }
                }

                bool foreground = !capturing && IsConnectedGameForeground();
                HashSet<string> keyboardActions = foreground ? ControlMatcher.Match(ControlCatalog.Actions.ToDictionary(action => action.Id, _profiles.Keyboard), keyboard) : [];
                HashSet<string> controllerActions = [];
                if (foreground)
                {
                    foreach (ControllerReading controller in controllers)
                        controllerActions.UnionWith(ControlMatcher.Match(ControlCatalog.Actions.ToDictionary(action => action.Id, action => _profiles.Controller(action, controller.Identity, controller.Mapped)), controller.Pressed));
                }

                HashSet<string> actions = [.. keyboardActions, .. controllerActions];
                DispatchShortcuts(actions, controllerActions);
                SendGameState(foreground, actions);
                _previousActions = actions;
                token.WaitHandle.WaitOne(_pollIntervalMilliseconds);
            }
        }
        finally
        {
            SendGameState(false, []);
        }
    }

    /// <summary>
    /// Publishes attachment changes and remembers each hardware profile once.
    /// </summary>
    /// <param name="controllers">The currently connected controller readings.</param>
    private void PublishControllers(IReadOnlyList<ControllerReading> controllers)
    {
        foreach (ControllerReading controller in controllers)
            _profiles.Remember(controller.Identity, controller.Name, controller.Mapped);

        bool changed;
        lock (_sync)
        {
            changed = !_controllers.Select(controller => controller.Identity).SequenceEqual(controllers.Select(controller => controller.Identity));
            _controllers = controllers;
        }

        if (changed)
            ControlsChanged?.Invoke();
    }

    /// <summary>
    /// Reports preference-write failure without stopping keyboard or controller polling.
    /// </summary>
    private void ReportBindingError()
    {
        if (BindingError is not null)
            return;

        BindingError = "The binding could not be saved. Check access to tracker preferences.";
        ControlsChanged?.Invoke();
    }

    /// <summary>
    /// Records the full physical chord and completes it on release.
    /// </summary>
    /// <param name="controllers">The currently connected controller readings.</param>
    private void Capture(IReadOnlyList<ControllerReading> controllers)
    {
        bool completed = false;
        bool changed = false;
        lock (_sync)
        {
            if (_captureAction is null || _captureController is null)
                return;

            ControllerReading? controller = controllers.FirstOrDefault(device => device.Identity == _captureController);
            bool held = controller?.Pressed.Count > 0;
            if (!_captureArmed)
            {
                _captureArmed = !held;
                return;
            }

            if (controller is null)
            {
                completed = true;
            }
            else
            {
                if (_captureReady && !held)
                    return;

                if (_captureReady)
                {
                    _capturedControls.Clear();
                    _captureReady = false;
                    changed = true;
                }

                int previousCount = _capturedControls.Count;
                _capturedControls.UnionWith(controller.Pressed);
                changed |= previousCount != _capturedControls.Count;
                if (!held && _capturedControls.Count > 0)
                {
                    _captureReady = true;
                    if (!_confirmCapture)
                        SaveCapture();

                    completed = true;
                }
            }

            if (controller is null)
            {
                _captureAction = null;
                _captureReady = false;
            }
        }

        if (completed || changed)
            ControlsChanged?.Invoke();
    }

    /// <summary>
    /// Dispatches tracker navigation only on the leading edge of an action.
    /// </summary>
    /// <param name="actions">The currently matched keyboard and controller actions.</param>
    /// <param name="controllerActions">The actions matched specifically by controllers.</param>
    private void DispatchShortcuts(HashSet<string> actions, HashSet<string> controllerActions)
    {
        foreach (string action in actions.Except(_previousActions))
        {
            TrackerView? view = ResolveView(action);

            if (view == TrackerView.Enemy && controllerActions.Contains(action))
            {
                EnemyCycleRequested?.Invoke();
            }
            else if (view is not null)
            {
                ViewRequested?.Invoke(view.Value);
            }
            else if (action == ControlCatalog.Reset && _connectionState.Snapshot.Game?.ControlOverridesSupported != true)
            {
                _ = RequestResetAsync();
            }
        }
    }

    /// <summary>
    /// Renews a complete input snapshot only on change or a short keepalive interval.
    /// </summary>
    /// <param name="enabled">Whether the tracker should own the connected game's input.</param>
    /// <param name="actions">The currently held game and tracker actions.</param>
    private void SendGameState(bool enabled, HashSet<string> actions)
    {
        if (_connectionState.Snapshot.Status != TrackerConnectionStatus.Connected || _connectionState.Snapshot.Game?.ControlOverridesSupported != true)
        {
            _overrideEnabled = false;
            _lastWrite = 0;
            return;
        }

        HashSet<string> gameActions = [.. actions.Where(action => !ControlCatalog.IsTrackerAction(action))];
        long now = Environment.TickCount64;
        if (enabled == _overrideEnabled && gameActions.SetEquals(_lastGameActions) && (!enabled || now - _lastWrite < _keepAliveMilliseconds))
            return;

        using CancellationTokenSource timeout = new(_writeTimeoutMilliseconds);
        try
        {
            ControlStatePayload payload = new(enabled, ControlCatalog.GameActions, [.. gameActions]);
            _connectionService.Requests.SendControlStateAsync(payload, timeout.Token).GetAwaiter().GetResult();
            _overrideEnabled = enabled;
            _lastGameActions = gameActions;
            _lastWrite = now;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or OperationCanceledException)
        {
            _overrideEnabled = false;
            _lastWrite = 0;
        }
    }

    /// <summary>
    /// Sends a guarded reset to older runtimes that do not support input snapshots.
    /// </summary>
    /// <returns>A task that completes after the reset request settles.</returns>
    private async Task RequestResetAsync()
    {
        try
        {
            await _connectionService.Requests.ResetRunAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
        }
    }

    /// <summary>
    /// Reads keyboard keys without mouse buttons, duplicate modifiers, or native F1/F12 controls.
    /// </summary>
    /// <returns>The currently held supported Windows virtual keys.</returns>
    private static HashSet<int> ReadKeyboard()
    {
        HashSet<int> pressed = [];
        for (int key = 8; key < 255; key++)
        {
            if (key is not (0x70 or 0x7B or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5) && (GetAsyncKeyState(key) & _pressedKeyMask) != 0)
                pressed.Add(key);
        }

        if (pressed.Contains(0x12) && pressed.Contains(0x0D))
        {
            pressed.Remove(0x12);
            pressed.Remove(0x0D);
        }

        return pressed;
    }

    /// <summary>
    /// Determines whether capture belongs to the foreground tracker process.
    /// </summary>
    /// <returns>Whether the tracker process owns the foreground window.</returns>
    private static bool IsTrackerForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint processId);
        return processId == Environment.ProcessId;
    }

    /// <summary>
    /// Determines whether the connected game installation owns the foreground process.
    /// </summary>
    /// <returns>Whether the connected game directory contains the foreground executable.</returns>
    private bool IsConnectedGameForeground()
    {
        string? gameRoot = _connectionState.Snapshot.Game?.GameRoot;
        if (_connectionState.Snapshot.Status != TrackerConnectionStatus.Connected || string.IsNullOrWhiteSpace(gameRoot))
            return false;

        GetWindowThreadProcessId(GetForegroundWindow(), out uint processId);
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            string? executable = process.MainModule?.FileName;
            string? directory = executable is null ? null : Path.GetDirectoryName(executable);
            return string.Equals(directory, Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the asynchronous state of a Windows virtual key.
    /// </summary>
    /// <param name="virtualKey">The Windows virtual key to query.</param>
    /// <returns>The packed pressed and transition state.</returns>
    [LibraryImport(_userLibrary)]
    private static partial short GetAsyncKeyState(int virtualKey);

    /// <summary>
    /// Gets the foreground native window.
    /// </summary>
    /// <returns>The foreground window handle.</returns>
    [LibraryImport(_userLibrary)]
    private static partial nint GetForegroundWindow();

    /// <summary>
    /// Gets the process owning a native window.
    /// </summary>
    /// <param name="window">The native window handle.</param>
    /// <param name="processId">Receives the identifier of the window's owning process.</param>
    /// <returns>The identifier of the window's owning thread.</returns>
    [LibraryImport(_userLibrary)]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
}
