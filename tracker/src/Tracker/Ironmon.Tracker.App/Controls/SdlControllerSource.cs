using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Hexa.NET.SDL2;

namespace Ironmon.Tracker.App.Controls;

/// <summary>
/// Reads mapped gamepads and generic joysticks on the input worker thread.
/// </summary>
public sealed unsafe class SdlControllerSource : IDisposable
{
    private const uint _inputSubsystems = 0x00000200 | 0x00002000;
    private const string _backgroundHint = "SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS";
    private const string _enabled = "1";
    private const int _axisThreshold = 20_000;
    private const string _identitySeparator = ":";
    private const string _identityFormat = "N";
    private static readonly string[] _buttonNames =
    [
        ControlInput.South,
        ControlInput.East,
        ControlInput.West,
        ControlInput.North,
        ControlInput.Back,
        ControlInput.Guide,
        ControlInput.Start,
        ControlInput.LeftStick,
        ControlInput.RightStick,
        ControlInput.LeftShoulder,
        ControlInput.RightShoulder,
        ControlInput.DpadUp,
        ControlInput.DpadDown,
        ControlInput.DpadLeft,
        ControlInput.DpadRight,
        ControlInput.Misc,
        ControlInput.Paddle1,
        ControlInput.Paddle2,
        ControlInput.Paddle3,
        ControlInput.Paddle4,
        ControlInput.Touchpad
    ];

    private static readonly string[] _axisNames =
    [
        ControlInput.LeftX,
        ControlInput.LeftY,
        ControlInput.RightX,
        ControlInput.RightY,
        ControlInput.LeftTrigger,
        ControlInput.RightTrigger
    ];

    private const string _positive = "+";
    private const string _negative = "-";
    private const string _rawButtonPrefix = "button_";
    private const string _rawAxisPrefix = "axis_";
    private const string _rawHatPrefix = "hat_";
    private static readonly string[] _hatDirections = [ControlCatalog.Up, ControlCatalog.Right, ControlCatalog.Down, ControlCatalog.Left];
    private readonly Dictionary<int, OpenController> _devices = [];
    private bool _initialized;

    /// <summary>
    /// Initializes SDL input without creating a second game window.
    /// </summary>
    public void Start()
    {
        SDL.SetHint(_backgroundHint, _enabled);
        if (SDL.InitSubSystem(_inputSubsystems) != 0)
            throw new InvalidOperationException(SDL.GetErrorS());

        _initialized = true;
    }

    /// <summary>
    /// Polls controllers and handles attachment and removal.
    /// </summary>
    /// <returns>The connected controllers and their currently held controls.</returns>
    public IReadOnlyList<ControllerReading> Read()
    {
        SDL.PumpEvents();
        SDLEvent pending = default;
        while (SDL.PollEvent(ref pending) != 0)
        {
        }

        SDL.JoystickUpdate();
        HashSet<int> attached = [];
        for (int index = 0; index < SDL.NumJoysticks(); index++)
        {
            int instance = SDL.JoystickGetDeviceInstanceID(index);
            if (instance < 0)
                continue;

            attached.Add(instance);
            if (!_devices.ContainsKey(instance))
                Open(index, instance);
        }

        foreach (int instance in _devices.Keys.Where(id => !attached.Contains(id)).ToArray())
        {
            Close(_devices[instance]);
            _devices.Remove(instance);
        }

        List<ControllerReading> readings = [];
        foreach (OpenController device in _devices.Values)
        {
            SDLJoystick* joystick = (SDLJoystick*)device.Joystick;
            if ((int)SDL.JoystickGetAttached(joystick) == 0)
                continue;

            HashSet<string> pressed = device.Controller != 0 ? ReadMapped((SDLGameController*)device.Controller) : ReadRaw(device);
            readings.Add(new(device.Identity, device.Name, device.Controller != 0, pressed));
        }

        return readings;
    }

    /// <summary>
    /// Opens one device and derives a reconnectable identity without using its transient instance number.
    /// </summary>
    /// <param name="index">The device's current SDL inventory index.</param>
    /// <param name="instance">The device's transient SDL instance identifier.</param>
    private void Open(int index, int instance)
    {
        SDLGameController* controller = (int)SDL.IsGameController(index) != 0 ? SDL.GameControllerOpen(index) : null;
        SDLJoystick* joystick = controller != null ? SDL.GameControllerGetJoystick(controller) : SDL.JoystickOpen(index);
        if (joystick == null)
            return;

        string name = Marshal.PtrToStringUTF8((nint)SDL.JoystickName(joystick)) ?? "Controller";
        string serial = Marshal.PtrToStringUTF8((nint)SDL.JoystickGetSerial(joystick)) ?? string.Empty;
        string path = Marshal.PtrToStringUTF8((nint)SDL.JoystickPath(joystick)) ?? string.Empty;
        string hardware = SDL.JoystickGetGUID(joystick).ToString(_identityFormat);
        string location = string.IsNullOrEmpty(serial) ? path : serial;
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hardware + _identitySeparator + name + _identitySeparator + location)));
        SDL.JoystickUpdate();
        int[] neutral = [.. Enumerable.Range(0, SDL.JoystickNumAxes(joystick)).Select(axis => (int)SDL.JoystickGetAxis(joystick, axis))];
        _devices[instance] = new((nint)joystick, (nint)controller, identity, name, neutral);
    }

    /// <summary>
    /// Reads standard gamepad controls using SDL's hardware mapping database.
    /// </summary>
    /// <param name="controller">The open mapped controller handle.</param>
    /// <returns>The held normalized buttons and axis directions.</returns>
    private static HashSet<string> ReadMapped(SDLGameController* controller)
    {
        HashSet<string> pressed = [];
        for (int button = 0; button < _buttonNames.Length; button++)
        {
            if (SDL.GameControllerGetButton(controller, (SDLGameControllerButton)button) != 0)
                pressed.Add(_buttonNames[button]);
        }

        for (int axis = 0; axis < _axisNames.Length; axis++)
        {
            int value = SDL.GameControllerGetAxis(controller, (SDLGameControllerAxis)axis);
            if (axis >= 4)
            {
                if (value > _axisThreshold)
                    pressed.Add(_axisNames[axis]);
            }
            else if (Math.Abs(value) > _axisThreshold)
            {
                if (axis is 2 or 3)
                {
                    int other = SDL.GameControllerGetAxis(controller, (SDLGameControllerAxis)(axis == 2 ? 3 : 2));
                    if (Math.Abs(other) > Math.Abs(value) || (axis == 3 && Math.Abs(other) == Math.Abs(value)))
                        continue;
                }

                pressed.Add(_axisNames[axis] + (value < 0 ? _negative : _positive));
            }
        }

        return pressed;
    }

    /// <summary>
    /// Reads unnamed buttons, calibrated axis directions, and directional hats.
    /// </summary>
    /// <param name="device">The open generic joystick and its neutral calibration.</param>
    /// <returns>The held raw buttons, axis directions, and hat directions.</returns>
    private static HashSet<string> ReadRaw(OpenController device)
    {
        SDLJoystick* joystick = (SDLJoystick*)device.Joystick;
        HashSet<string> pressed = [];
        for (int button = 0; button < SDL.JoystickNumButtons(joystick); button++)
        {
            if (SDL.JoystickGetButton(joystick, button) != 0)
                pressed.Add(_rawButtonPrefix + button);
        }

        for (int axis = 0; axis < device.Neutral.Length; axis++)
        {
            int value = SDL.JoystickGetAxis(joystick, axis) - device.Neutral[axis];
            if (Math.Abs(value) > _axisThreshold)
                pressed.Add(_rawAxisPrefix + axis + (value < 0 ? _negative : _positive));
        }

        for (int hat = 0; hat < SDL.JoystickNumHats(joystick); hat++)
        {
            byte value = SDL.JoystickGetHat(joystick, hat);
            for (int direction = 0; direction < _hatDirections.Length; direction++)
            {
                if ((value & (1 << direction)) != 0)
                    pressed.Add(_rawHatPrefix + hat + _identitySeparator + _hatDirections[direction]);
            }
        }

        return pressed;
    }

    /// <summary>
    /// Closes native handles without closing a controller-owned joystick twice.
    /// </summary>
    /// <param name="device">The device whose native handles should close.</param>
    private static void Close(OpenController device)
    {
        if (device.Controller != 0)
        {
            SDL.GameControllerClose((SDLGameController*)device.Controller);
        }
        else
        {
            SDL.JoystickClose((SDLJoystick*)device.Joystick);
        }
    }

    /// <summary>
    /// Closes all devices and the SDL input subsystems on their owning thread.
    /// </summary>
    public void Dispose()
    {
        foreach (OpenController device in _devices.Values)
            Close(device);

        _devices.Clear();
        if (_initialized)
            SDL.QuitSubSystem(_inputSubsystems);

        _initialized = false;
    }

    /// <summary>
    /// Owns one open SDL joystick and its optional mapped controller.
    /// </summary>
    /// <remarks>
    /// Initializes device ownership and neutral axis calibration.
    /// </remarks>
    /// <param name="joystick">The open joystick handle.</param>
    /// <param name="controller">The optional mapped controller handle.</param>
    /// <param name="identity">The persistent profile identity.</param>
    /// <param name="name">The hardware display name.</param>
    /// <param name="neutral">The axis readings captured when the device opened.</param>
    private sealed class OpenController(nint joystick, nint controller, string identity, string name, int[] neutral)
    {
        /// <summary>
        /// Gets the joystick handle.
        /// </summary>
        public nint Joystick { get; } = joystick;

        /// <summary>
        /// Gets the mapped controller handle when available.
        /// </summary>
        public nint Controller { get; } = controller;

        /// <summary>
        /// Gets the persistent profile identity.
        /// </summary>
        public string Identity { get; } = identity;

        /// <summary>
        /// Gets the hardware display name.
        /// </summary>
        public string Name { get; } = name;

        /// <summary>
        /// Gets the initial neutral axis readings.
        /// </summary>
        public int[] Neutral { get; } = neutral;
    }
}

/// <summary>
/// Describes the current physical controls held on one connected device.
/// </summary>
/// <remarks>
/// Initializes an immutable device reading.
/// </remarks>
/// <param name="identity">The persistent profile identity.</param>
/// <param name="name">The controller's display name.</param>
/// <param name="mapped">Whether SDL provides a standard gamepad mapping.</param>
/// <param name="pressed">The currently held physical controls.</param>
public sealed class ControllerReading(string identity, string name, bool mapped, IReadOnlySet<string> pressed)
{
    /// <summary>
    /// Gets the persistent profile identity.
    /// </summary>
    public string Identity { get; } = identity;

    /// <summary>
    /// Gets the hardware name.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets whether the device has a standard SDL mapping.
    /// </summary>
    public bool Mapped { get; } = mapped;

    /// <summary>
    /// Gets the controls currently pressed.
    /// </summary>
    public IReadOnlySet<string> Pressed { get; } = pressed;
}
