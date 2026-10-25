using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Ironmon.Tracker.AchievementOverlay;

/// <summary>
/// Reads the intended game's physical client rectangle and positions only renderer-owned windows.
/// </summary>
internal static partial class NativeGameWindow
{
    private const string _userLibrary = "user32.dll";
    private const string _readStylesEntry = "GetWindowLongPtrW";
    private const int _extendedStyleIndex = -20;
    private const long _overlayStyles = 0x00000008L | 0x00080000L | 0x00000020L | 0x00000080L | 0x08000000L;
    private const uint _positionFlags = 0x0010 | 0x0200;
    private static readonly string[] _executables = ["InfiniteFusion2", "InfiniteFusion2-performance"];

    /// <summary>
    /// Finds exactly one game window belonging to the selected installation.
    /// </summary>
    /// <param name="root">The absolute game installation directory.</param>
    /// <returns>The unique native game window.</returns>
    internal static nint Find(string root)
    {
        List<nint> windows = [];
        foreach (string name in _executables)
        {
            Process[] processes = Process.GetProcessesByName(name);
            try
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        string? executable = process.MainModule?.FileName;
                        if (executable is not null && string.Equals(Path.GetDirectoryName(executable), Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase) && process.MainWindowHandle != 0)
                            windows.Add(process.MainWindowHandle);
                    }
                    catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                    }
                }
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
        }

        return windows.Count == 1 ? windows[0] : throw new InvalidOperationException("Open one game from the selected installation before showing the overlay.");
    }

    /// <summary>
    /// Determines whether the selected game is alive and currently owns input focus.
    /// </summary>
    /// <param name="window">The selected game window.</param>
    /// <returns>True only for the visible, non-minimized foreground game.</returns>
    internal static bool IsForegroundGame(nint window) => IsWindow(window) && IsWindowVisible(window) && !IsIconic(window) && GetForegroundWindow() == window;

    /// <summary>
    /// Reads the current game viewport without changing its size, settings, focus, or contents.
    /// </summary>
    /// <param name="window">The selected native game window.</param>
    /// <returns>The physical notification placement, or null for a closed or zero-size window.</returns>
    internal static OverlayPlacement? Measure(nint window)
    {
        if (!IsWindow(window) || !GetClientRect(window, out NativeRect rectangle))
            return null;

        NativePoint origin = default;
        int width = rectangle.Right - rectangle.Left;
        int height = rectangle.Bottom - rectangle.Top;
        return width > 0 && height > 0 && ClientToScreen(window, ref origin) ? OverlayPlacement.FromClient(origin.X, origin.Y, width, height) : null;
    }

    /// <summary>
    /// Reads physical client dimensions independently of the game's aspect-ratio calculation.
    /// </summary>
    /// <param name="window">The renderer-owned window being checked.</param>
    /// <returns>The client width and height in physical pixels.</returns>
    internal static (int Width, int Height) ClientPixels(nint window)
    {
        if (!GetClientRect(window, out NativeRect rectangle))
            throw new InvalidOperationException("The preview window is no longer available.");

        return (rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
    }

    /// <summary>
    /// Positions only the renderer's overlay in physical desktop pixels without activation.
    /// </summary>
    /// <param name="overlay">The renderer-owned native window.</param>
    /// <param name="placement">The current game-aligned bounds.</param>
    internal static void PlaceOverlay(nint overlay, OverlayPlacement placement)
    {
        if (!SetWindowPos(overlay, (nint)(-1), placement.X, placement.Y, placement.Width, placement.Height, _positionFlags))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }

    /// <summary>
    /// Reads styles for native nonactivation and click-through acceptance checks.
    /// </summary>
    /// <param name="overlay">The renderer-owned native window.</param>
    /// <returns>True when all required noninteractive styles remain set.</returns>
    internal static bool HasOverlayStyles(nint overlay) => (GetWindowLongPtr(overlay, _extendedStyleIndex).ToInt64() & _overlayStyles) == _overlayStyles;

    /// <summary>
    /// Represents a physical native rectangle.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    /// <summary>
    /// Represents a physical native screen point.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    /// <summary>
    /// Tests a native window's lifetime.
    /// </summary>
    /// <param name="window">The native window.</param>
    /// <returns>Whether the handle still identifies a window.</returns>
    [LibraryImport(_userLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint window);

    /// <summary>
    /// Reads whether the window is visible.
    /// </summary>
    /// <param name="window">The native window.</param>
    /// <returns>Whether it is marked visible.</returns>
    [LibraryImport(_userLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    /// <summary>
    /// Reads whether the window is minimized.
    /// </summary>
    /// <param name="window">The native window.</param>
    /// <returns>Whether it is minimized.</returns>
    [LibraryImport(_userLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    /// <summary>
    /// Reads the current foreground window without activating anything.
    /// </summary>
    /// <returns>The native foreground handle.</returns>
    [LibraryImport(_userLibrary)]
    internal static partial nint GetForegroundWindow();

    /// <summary>
    /// Reads a window's client bounds.
    /// </summary>
    /// <param name="window">The native window.</param>
    /// <param name="rectangle">Receives the client rectangle.</param>
    /// <returns>Whether the read succeeded.</returns>
    [LibraryImport(_userLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint window, out NativeRect rectangle);

    /// <summary>
    /// Converts a client point to physical desktop coordinates.
    /// </summary>
    /// <param name="window">The native window.</param>
    /// <param name="point">The client point updated to screen coordinates.</param>
    /// <returns>Whether the conversion succeeded.</returns>
    [LibraryImport(_userLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint window, ref NativePoint point);

    /// <summary>
    /// Reads pointer-sized window styles.
    /// </summary>
    /// <param name="window">The native window.</param>
    /// <param name="index">The style selector.</param>
    /// <returns>The selected value.</returns>
    [LibraryImport(_userLibrary, EntryPoint = _readStylesEntry)]
    private static partial nint GetWindowLongPtr(nint window, int index);

    /// <summary>
    /// Sets physical bounds on the renderer-owned window without activation.
    /// </summary>
    /// <param name="window">The renderer-owned window.</param>
    /// <param name="insertAfter">The requested z order.</param>
    /// <param name="x">Physical desktop x coordinate.</param>
    /// <param name="y">Physical desktop y coordinate.</param>
    /// <param name="width">Physical pixel width.</param>
    /// <param name="height">Physical pixel height.</param>
    /// <param name="flags">Native nonactivation and owner-order flags.</param>
    /// <returns>Whether positioning succeeded.</returns>
    [LibraryImport(_userLibrary, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
