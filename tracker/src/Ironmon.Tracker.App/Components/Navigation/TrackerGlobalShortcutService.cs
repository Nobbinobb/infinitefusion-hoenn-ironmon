using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ironmon.Tracker.App.Components.Navigation;

/// <summary>
/// Monitors foreground-safe keyboard and controller shortcuts for tracker navigation.
/// </summary>
public sealed partial class TrackerGlobalShortcutService
{
    private const int PollIntervalMilliseconds = 16;
    private const int PressedKeyMask = 0x8000;
    private const int ControlVirtualKey = 0x11;
    private const int OneVirtualKey = 0x31;
    private const int TwoVirtualKey = 0x32;
    private const int ThreeVirtualKey = 0x33;
    private const int FourVirtualKey = 0x34;
    private const ushort StartButton = 0x0010;
    private const byte TriggerThreshold = 192;
    private const int RightStickThreshold = 20_000;

    private readonly TrackerConnectionState _connectionState;
    private readonly TrackerConnectionService _connectionService;
    private readonly Lock _sync = new();
    private readonly bool[] _keyboardLatches = new bool[4];
    private readonly TrackerView?[] _controllerDirections = new TrackerView?[4];
    private readonly bool[] _controllerResetLatches = new bool[4];
    private CancellationTokenSource? _cancellation;
    private Task? _pollTask;
    private nint _cachedForegroundWindow;
    private string? _cachedGameRoot;
    private bool _cachedGameForeground;
    private bool _xInputAvailable = true;

    /// <summary>
    /// Initializes the global shortcut monitor.
    /// </summary>
    /// <param name="connectionState">The connection state containing the active game directory.</param>
    /// <param name="connectionService">The service used to send guarded requests to the connected game.</param>
    public TrackerGlobalShortcutService(TrackerConnectionState connectionState, TrackerConnectionService connectionService)
    {
        ArgumentNullException.ThrowIfNull(connectionState);
        ArgumentNullException.ThrowIfNull(connectionService);
        _connectionState = connectionState;
        _connectionService = connectionService;
    }

    /// <summary>
    /// Occurs when a foreground-safe shortcut requests a tracker view.
    /// </summary>
    public event Action<TrackerView>? ViewRequested;

    /// <summary>
    /// Occurs when the controller's Enemy shortcut requests the next active opposing card.
    /// </summary>
    public event Action? EnemyCycleRequested;

    /// <summary>
    /// Starts monitoring keyboard and controller state.
    /// </summary>
    public void Start()
    {
        lock (_sync)
        {
            if (_pollTask is not null)
                return;

            _cancellation = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollAsync(_cancellation.Token));
        }
    }

    /// <summary>
    /// Stops shortcut monitoring and waits for the polling loop to finish.
    /// </summary>
    /// <returns>A task that completes after monitoring has stopped.</returns>
    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? pollTask;
        lock (_sync)
        {
            cancellation = _cancellation;
            pollTask = _pollTask;
            _cancellation = null;
            _pollTask = null;
        }

        if (cancellation is null || pollTask is null)
            return;

        cancellation.Cancel();
        try
        {
            await pollTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            ResetLatches();
            cancellation.Dispose();
        }
    }

    /// <summary>
    /// Polls supported input devices until monitoring is stopped.
    /// </summary>
    /// <param name="cancellationToken">The token that stops the polling loop.</param>
    /// <returns>A task representing the polling lifetime.</returns>
    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(PollIntervalMilliseconds));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!IsConnectedGameForeground())
            {
                ResetLatches();
                continue;
            }

            PollKeyboard();
            PollControllers();
        }
    }

    /// <summary>
    /// Polls the Ctrl+1 through Ctrl+4 keyboard shortcuts.
    /// </summary>
    private void PollKeyboard()
    {
        bool controlPressed = IsVirtualKeyPressed(ControlVirtualKey);
        PollKeyboardShortcut(0, controlPressed && IsVirtualKeyPressed(OneVirtualKey), TrackerView.Player);
        PollKeyboardShortcut(1, controlPressed && IsVirtualKeyPressed(TwoVirtualKey), TrackerView.Enemy);
        PollKeyboardShortcut(2, controlPressed && IsVirtualKeyPressed(ThreeVirtualKey), TrackerView.Lookup);
        PollKeyboardShortcut(3, controlPressed && IsVirtualKeyPressed(FourVirtualKey), TrackerView.Archive);
    }

    /// <summary>
    /// Emits a keyboard shortcut once per press.
    /// </summary>
    /// <param name="index">The shortcut latch index.</param>
    /// <param name="pressed">Whether the complete shortcut is pressed.</param>
    /// <param name="view">The requested tracker view.</param>
    private void PollKeyboardShortcut(int index, bool pressed, TrackerView view)
    {
        if (pressed && !_keyboardLatches[index])
            ViewRequested?.Invoke(view);

        _keyboardLatches[index] = pressed;
    }

    /// <summary>
    /// Polls connected XInput controllers for the dual-trigger right-stick chord.
    /// </summary>
    private void PollControllers()
    {
        if (!_xInputAvailable)
            return;

        try
        {
            for (uint index = 0; index < _controllerDirections.Length; index++)
                PollController(index);
        }
        catch (DllNotFoundException)
        {
            _xInputAvailable = false;
        }
        catch (EntryPointNotFoundException)
        {
            _xInputAvailable = false;
        }
    }

    /// <summary>
    /// Polls one XInput controller and emits a changed stick direction once.
    /// </summary>
    /// <param name="index">The zero-based XInput controller index.</param>
    private void PollController(uint index)
    {
        if (XInputGetState(index, out XInputState state) != 0)
        {
            _controllerDirections[index] = null;
            SetControllerResetState(index, false);
            return;
        }

        XInputGamepad gamepad = state.Gamepad;
        bool chordPressed = gamepad.LeftTrigger >= TriggerThreshold && gamepad.RightTrigger >= TriggerThreshold;
        bool resetPressed = chordPressed && (gamepad.Buttons & StartButton) != 0;
        SetControllerResetState(index, resetPressed);

        TrackerView? direction = chordPressed && !resetPressed ? ResolveRightStickDirection(gamepad.RightThumbX, gamepad.RightThumbY) : null;
        if (direction is not null && direction != _controllerDirections[index])
        {
            if (direction == TrackerView.Enemy)
            {
                EnemyCycleRequested?.Invoke();
            }
            else
            {
                ViewRequested?.Invoke(direction.Value);
            }
        }

        _controllerDirections[index] = direction;
    }

    /// <summary>
    /// Updates one controller's reset state and mirrors the aggregate state to F7.
    /// </summary>
    /// <param name="index">The zero-based XInput controller index.</param>
    /// <param name="pressed">Whether the complete reset chord is pressed.</param>
    private void SetControllerResetState(uint index, bool pressed)
    {
        bool wasPressed = Array.Exists(_controllerResetLatches, static state => state);
        _controllerResetLatches[index] = pressed;
        bool isPressed = Array.Exists(_controllerResetLatches, static state => state);
        if (!wasPressed && isPressed)
            _ = RequestResetAsync();
    }

    /// <summary>
    /// Sends a guarded reset request directly to the connected game runtime.
    /// </summary>
    /// <returns>A task representing the request.</returns>
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
    /// Resolves a right-stick direction to its tracker view.
    /// </summary>
    /// <param name="horizontal">The signed horizontal stick position.</param>
    /// <param name="vertical">The signed vertical stick position.</param>
    /// <returns>The requested view, or <see langword="null"/> while the stick is neutral.</returns>
    private static TrackerView? ResolveRightStickDirection(short horizontal, short vertical)
    {
        int horizontalMagnitude = Math.Abs((int)horizontal);
        int verticalMagnitude = Math.Abs((int)vertical);
        if (Math.Max(horizontalMagnitude, verticalMagnitude) < RightStickThreshold)
            return null;

        if (horizontalMagnitude >= verticalMagnitude)
            return horizontal < 0 ? TrackerView.Player : TrackerView.Enemy;

        return vertical > 0 ? TrackerView.Lookup : TrackerView.Archive;
    }

    /// <summary>
    /// Determines whether the connected Infinite Fusion executable owns the foreground window.
    /// </summary>
    /// <returns><see langword="true"/> only while the connected game is foreground.</returns>
    private bool IsConnectedGameForeground()
    {
        string? gameRoot = _connectionState.Snapshot.Game?.GameRoot;
        nint foregroundWindow = GetForegroundWindow();
        if (string.IsNullOrWhiteSpace(gameRoot) || foregroundWindow == 0)
            return false;

        if (foregroundWindow == _cachedForegroundWindow && string.Equals(gameRoot, _cachedGameRoot, StringComparison.OrdinalIgnoreCase))
            return _cachedGameForeground;

        _cachedForegroundWindow = foregroundWindow;
        _cachedGameRoot = gameRoot;
        _cachedGameForeground = IsWindowOwnedByGame(foregroundWindow, gameRoot);
        return _cachedGameForeground;
    }

    /// <summary>
    /// Compares a window's executable directory with the connected game directory.
    /// </summary>
    /// <param name="window">The native foreground window handle.</param>
    /// <param name="gameRoot">The connected Infinite Fusion directory.</param>
    /// <returns><see langword="true"/> when the window belongs to that game installation.</returns>
    private static bool IsWindowOwnedByGame(nint window, string gameRoot)
    {
        _ = GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0)
            return false;

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            string? executable = process.MainModule?.FileName;
            string? executableDirectory = executable is null ? null : Path.GetDirectoryName(Path.GetFullPath(executable));
            string normalizedGameRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
            return string.Equals(executableDirectory, normalizedGameRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Clears edge-detection state after the connected game loses focus.
    /// </summary>
    private void ResetLatches()
    {
        Array.Clear(_keyboardLatches);
        Array.Clear(_controllerDirections);
        Array.Clear(_controllerResetLatches);
    }

    /// <summary>
    /// Determines whether a Windows virtual key is currently pressed.
    /// </summary>
    /// <param name="virtualKey">The Windows virtual-key identifier.</param>
    /// <returns><see langword="true"/> when the key is pressed.</returns>
    private static bool IsVirtualKeyPressed(int virtualKey) => (GetAsyncKeyState(virtualKey) & PressedKeyMask) != 0;

    /// <summary>
    /// Gets the current asynchronous state of a virtual key.
    /// </summary>
    /// <param name="virtualKey">The Windows virtual-key identifier.</param>
    /// <returns>The native key-state flags.</returns>
    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);

    /// <summary>
    /// Gets the foreground native window.
    /// </summary>
    /// <returns>The foreground window handle.</returns>
    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    /// <summary>
    /// Gets the process owning a native window.
    /// </summary>
    /// <param name="window">The native window handle.</param>
    /// <param name="processId">Receives the owning process identifier.</param>
    /// <returns>The identifier of the thread that created the window.</returns>
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    /// <summary>
    /// Reads the latest state for an XInput controller.
    /// </summary>
    /// <param name="userIndex">The zero-based XInput controller index.</param>
    /// <param name="state">Receives the controller state.</param>
    /// <returns>Zero when a connected controller was read successfully.</returns>
    [LibraryImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static partial uint XInputGetState(uint userIndex, out XInputState state);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }
}
