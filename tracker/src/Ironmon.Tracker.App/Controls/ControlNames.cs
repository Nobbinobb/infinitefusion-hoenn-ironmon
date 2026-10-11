using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components.Web;

namespace Ironmon.Tracker.App.Controls;

/// <summary>
/// Provides readable keyboard and gamepad names for the controls editor.
/// </summary>
public static partial class ControlNames
{
    private const string _userLibrary = "user32.dll";
    private const string _keyboardNameEntry = "GetKeyNameTextW";
    private const string _mapKeyEntry = "MapVirtualKeyW";
    private const string _underscore = "_";
    private const string _space = " ";
    private const string _characterKeyEntry = "VkKeyScanW";
    private const string _controlKey = "Control";
    private const string _shiftKey = "Shift";
    private const string _altKey = "Alt";
    private const string _enterKey = "Enter";
    private const string _escapeKey = "Escape";
    private const string _arrowLeftKey = "ArrowLeft";
    private const string _arrowRightKey = "ArrowRight";
    private const string _arrowUpKey = "ArrowUp";
    private const string _arrowDownKey = "ArrowDown";
    private const string _tabKey = "Tab";
    private const string _backspaceKey = "Backspace";
    private const string _deleteKey = "Delete";
    private const string _insertKey = "Insert";
    private const string _homeKey = "Home";
    private const string _endKey = "End";
    private const string _pageUpKey = "PageUp";
    private const string _pageDownKey = "PageDown";
    private const string _numpadPrefix = "Numpad";
    private const string _numpadDecimalCode = "NumpadDecimal";
    private const string _numpadAddCode = "NumpadAdd";
    private const string _numpadSubtractCode = "NumpadSubtract";
    private const string _numpadMultiplyCode = "NumpadMultiply";
    private const string _numpadDivideCode = "NumpadDivide";
    private const string _capsLockKey = "CapsLock";
    private const string _numLockKey = "NumLock";
    private const string _scrollLockKey = "ScrollLock";
    private const string _pauseKey = "Pause";
    private const string _printScreenKey = "PrintScreen";
    private const string _contextMenuKey = "ContextMenu";
    private const string _dualSenseName = "DualSense";
    private const string _dualShockName = "DualShock";
    private const string _playStationName = "PlayStation";
    private const string _ps4Name = "PS4";
    private const string _ps5Name = "PS5";
    private const string _wirelessControllerName = "Wireless Controller";
    private static readonly string[] _playStationNames = [_dualSenseName, _dualShockName, _playStationName, _ps4Name, _ps5Name, _wirelessControllerName];

    /// <summary>
    /// Identifies controller names associated with the PlayStation button layout.
    /// </summary>
    /// <param name="name">The SDL device name.</param>
    /// <returns>Whether to display PlayStation symbols.</returns>
    public static bool IsPlayStation(string? name)
        => name is not null && _playStationNames.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Formats one controller control for the selected device family.
    /// </summary>
    /// <param name="token">The normalized physical control.</param>
    /// <param name="playStation">Whether the device uses PlayStation symbols.</param>
    /// <returns>The readable device-specific control name.</returns>
    public static string Controller(string token, bool playStation) => token switch
    {
        ControlInput.South => playStation ? "Cross" : "A",
        ControlInput.East => playStation ? "Circle" : "B",
        ControlInput.West => playStation ? "Square" : "X",
        ControlInput.North => playStation ? "Triangle" : "Y",
        ControlInput.Back => playStation ? "Share" : "Back",
        ControlInput.Start => playStation ? "Options" : "Start",
        ControlInput.Guide => playStation ? "PS" : "Guide",
        ControlInput.LeftShoulder => playStation ? "L1" : "LB",
        ControlInput.RightShoulder => playStation ? "R1" : "RB",
        ControlInput.LeftTrigger => playStation ? "L2" : "LT",
        ControlInput.RightTrigger => playStation ? "R2" : "RT",
        ControlInput.LeftStick => playStation ? "L3" : "LS click",
        ControlInput.RightStick => playStation ? "R3" : "RS click",
        _ => Controller(token)
    };

    /// <summary>
    /// Resolves a browser key event to the Windows virtual key used by global bindings.
    /// </summary>
    /// <param name="args">The browser keyboard event.</param>
    /// <returns>The virtual key, or null for an unsupported or reserved key.</returns>
    public static int? EventKey(KeyboardEventArgs args)
    {
        int? key = args.Key switch
        {
            _controlKey => 0x11,
            _shiftKey => 0x10,
            _altKey => 0x12,
            _enterKey => 0x0D,
            _escapeKey => 0x1B,
            _arrowLeftKey => 0x25,
            _arrowUpKey => 0x26,
            _arrowRightKey => 0x27,
            _arrowDownKey => 0x28,
            _tabKey => 0x09,
            _backspaceKey => 0x08,
            _deleteKey => 0x2E,
            _insertKey => 0x2D,
            _homeKey => 0x24,
            _endKey => 0x23,
            _pageUpKey => 0x21,
            _pageDownKey => 0x22,
            _capsLockKey => 0x14,
            _numLockKey => 0x90,
            _scrollLockKey => 0x91,
            _pauseKey => 0x13,
            _printScreenKey => 0x2C,
            _contextMenuKey => 0x5D,
            _ => null
        };

        int? numpadOperator = args.Code switch
        {
            _numpadDecimalCode => 0x6E,
            _numpadAddCode => 0x6B,
            _numpadSubtractCode => 0x6D,
            _numpadMultiplyCode => 0x6A,
            _numpadDivideCode => 0x6F,
            _ => null
        };

        if (numpadOperator is not null)
        {
            key = numpadOperator;
        }
        else if (args.Code.StartsWith(_numpadPrefix, StringComparison.Ordinal) && args.Code.Length == 7 && char.IsDigit(args.Code[^1]))
        {
            key = 0x60 + args.Code[^1] - '0';
        }
        else if (args.Key.Length == 1)
        {
            key = VkKeyScan(args.Key[0]) & 0xFF;
        }
        else if (args.Key.Length >= 2 && args.Key[0] == 'F' && int.TryParse(args.Key.AsSpan(1), out int function) && function is >= 1 and <= 24)
        {
            key = 0x6F + function;
        }

        return key is >= 8 and < 255 && key is not (0x70 or 0x7B) ? key : null;
    }

    /// <summary>
    /// Gets all modifiers explicitly held by a browser key event.
    /// </summary>
    /// <param name="args">The browser keyboard event.</param>
    /// <returns>The held Control, Shift, and Alt virtual keys.</returns>
    public static HashSet<int> EventModifiers(KeyboardEventArgs args)
    {
        HashSet<int> modifiers = [];
        if (args.CtrlKey)
            modifiers.Add(0x11);

        if (args.ShiftKey)
            modifiers.Add(0x10);

        if (args.AltKey)
            modifiers.Add(0x12);

        return modifiers;
    }

    /// <summary>
    /// Formats a physical controller token using common Xbox and PlayStation labels.
    /// </summary>
    /// <param name="token">The physical control identifier.</param>
    /// <returns>The controller control's display name.</returns>
    public static string Controller(string token) => token switch
    {
        ControlInput.South => "A / Cross",
        ControlInput.East => "B / Circle",
        ControlInput.West => "X / Square",
        ControlInput.North => "Y / Triangle",
        ControlInput.Back => "Back / Share",
        ControlInput.Start => "Start / Options",
        ControlInput.Guide => "Guide / PS",
        ControlInput.LeftShoulder => "LB / L1",
        ControlInput.RightShoulder => "RB / R1",
        ControlInput.LeftTrigger => "LT / L2",
        ControlInput.RightTrigger => "RT / R2",
        ControlInput.LeftStick => "Left stick press",
        ControlInput.RightStick => "Right stick press",
        ControlInput.LeftXNegative => "Left stick left",
        ControlInput.LeftXPositive => "Left stick right",
        ControlInput.LeftYNegative => "Left stick up",
        ControlInput.LeftYPositive => "Left stick down",
        ControlInput.RightXNegative => "Right stick left",
        ControlInput.RightXPositive => "Right stick right",
        ControlInput.RightYNegative => "Right stick up",
        ControlInput.RightYPositive => "Right stick down",
        ControlInput.DpadUp => "D-pad up",
        ControlInput.DpadDown => "D-pad down",
        ControlInput.DpadLeft => "D-pad left",
        ControlInput.DpadRight => "D-pad right",
        _ => token.Replace(_underscore, _space)
    };

    /// <summary>
    /// Formats a Windows virtual key using the current keyboard's key names.
    /// </summary>
    /// <param name="key">The Windows virtual key.</param>
    /// <returns>The key's display name.</returns>
    public static string Keyboard(int key) => key switch
    {
        0x10 => "Shift",
        0x11 => "Ctrl",
        0x12 => "Alt",
        0x0D => "Enter",
        0x20 => "Space",
        0x1B => "Esc",
        0x21 => "Page Up",
        0x22 => "Page Down",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x70 and <= 0x87 => $"F{key - 0x6F}",
        _ => NativeKeyboardName(key)
    };

    /// <summary>
    /// Resolves numpad and layout-dependent OEM key names.
    /// </summary>
    /// <param name="key">The Windows virtual key.</param>
    /// <returns>The native key name, or a hexadecimal fallback.</returns>
    private static unsafe string NativeKeyboardName(int key)
    {
        char* name = stackalloc char[64];
        uint scan = MapVirtualKey((uint)key, 0);
        int length = GetKeyNameText((int)(scan << 16), name, 64);
        return length > 0 ? new string(name, 0, length) : $"Key {key:X2}";
    }

    /// <summary>
    /// Maps a virtual key to its keyboard scan code.
    /// </summary>
    /// <param name="code">The virtual key or scan code to translate.</param>
    /// <param name="mapType">The Windows translation mode.</param>
    /// <returns>The translated key code.</returns>
    [LibraryImport(_userLibrary, EntryPoint = _mapKeyEntry)]
    private static partial uint MapVirtualKey(uint code, uint mapType);

    /// <summary>
    /// Reads the display name belonging to a keyboard scan code.
    /// </summary>
    /// <param name="scanCode">The scan code and key-name flags.</param>
    /// <param name="name">The destination character buffer.</param>
    /// <param name="capacity">The buffer's character capacity.</param>
    /// <returns>The number of characters written.</returns>
    [LibraryImport(_userLibrary, EntryPoint = _keyboardNameEntry)]
    private static unsafe partial int GetKeyNameText(int scanCode, char* name, int capacity);

    /// <summary>
    /// Resolves a character to the active keyboard layout's virtual key.
    /// </summary>
    /// <param name="character">The character to translate.</param>
    /// <returns>The packed virtual key and modifier state, or minus one when unmapped.</returns>
    [LibraryImport(_userLibrary, EntryPoint = _characterKeyEntry)]
    private static partial short VkKeyScan(ushort character);
}
