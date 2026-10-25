using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Ironmon.Tracker.AchievementOverlay;

/// <summary>
/// Provides the small Win32 surface needed for a transparent, nonactivating notification and its message pump.
/// </summary>
internal static partial class OverlayNative
{
    internal const uint WorkMessage = 0x8001;
    internal const uint TimerMessage = 0x0113;
    private const uint _quitMessage = 0x0012;
    private const uint _windowStyles = 0x80000000;
    private const uint _extendedStyles = 0x00000008 | 0x00080000 | 0x00000020 | 0x00000080 | 0x08000000;
    private const string _windowClass = "STATIC";
    private const string _windowTitle = "Achievement notification";
    private const string _userLibrary = "user32.dll";
    private const string _gdiLibrary = "gdi32.dll";
    private const string _kernelLibrary = "kernel32.dll";
    private const string _createWindowEntry = "CreateWindowExW";
    private const string _postMessageEntry = "PostThreadMessageW";
    private const string _getMessageEntry = "GetMessageW";
    private const string _peekMessageEntry = "PeekMessageW";
    private const string _dispatchMessageEntry = "DispatchMessageW";

    /// <summary>
    /// Creates only an overlay-owned layered window, initially hidden and off screen.
    /// </summary>
    /// <param name="owner">The selected game window, or zero for an owned test fixture.</param>
    /// <returns>The new native window handle.</returns>
    internal static nint CreateWindow(nint owner)
    {
        nint handle = CreateWindowEx(_extendedStyles, _windowClass, _windowTitle, _windowStyles, -32000, -32000, 1, 1, owner, 0, 0, 0);
        return handle != 0 ? handle : throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    /// <summary>
    /// Uploads premultiplied BGRA pixels and positions the small window without activating it.
    /// </summary>
    /// <param name="window">The overlay-owned native window.</param>
    /// <param name="source">The memory device context containing the current frame.</param>
    /// <param name="placement">The physical desktop bounds.</param>
    internal static void Present(nint window, nint source, OverlayPlacement placement)
    {
        NativePoint destination = new() { X = placement.X, Y = placement.Y };
        NativePoint origin = default;
        NativeSize size = new() { Width = placement.Width, Height = placement.Height };
        BlendFunction blend = new() { SourceConstantAlpha = 255, AlphaFormat = 1 };
        if (!UpdateLayeredWindow(window, 0, ref destination, ref size, source, ref origin, 0, ref blend, 2))
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        ShowWindow(window, 4);
    }

    /// <summary>
    /// Initializes the thread's message queue before other threads may post work.
    /// </summary>
    internal static void InitializeThread()
    {
        if (SetThreadDpiAwarenessContext((nint)(-4)) == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        PeekMessage(out _, 0, 0, 0, 0);
    }

    /// <summary>
    /// Requests orderly shutdown of an overlay-owned thread.
    /// </summary>
    /// <param name="thread">The renderer's native thread identity.</param>
    internal static void Quit(uint thread) => PostThreadMessage(thread, _quitMessage, 0, 0);

    /// <summary>
    /// Represents one message from the renderer-owned Windows queue.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        internal nint Window;
        internal uint Id;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal NativePoint Point;
        internal uint Private;
    }

    /// <summary>
    /// Represents a physical screen point.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    /// <summary>
    /// Represents a physical surface size.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        internal int Width;
        internal int Height;
    }

    /// <summary>
    /// Describes per-pixel premultiplied alpha for the desktop compositor.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        internal byte BlendOp;
        internal byte BlendFlags;
        internal byte SourceConstantAlpha;
        internal byte AlphaFormat;
    }

    /// <summary>
    /// Describes a top-down, uncompressed 32-bit native bitmap.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        internal uint Size;
        internal int Width;
        internal int Height;
        internal ushort Planes;
        internal ushort BitCount;
        internal uint Compression;
        internal uint ImageSize;
        internal int XPixelsPerMeter;
        internal int YPixelsPerMeter;
        internal uint ColorsUsed;
        internal uint ColorsImportant;
    }

    /// <summary>
    /// Creates the layered window using a system-provided class rather than a UI framework.
    /// </summary>
    /// <param name="extended">The extended styles.</param>
    /// <param name="className">The built-in class.</param>
    /// <param name="title">The accessibility title.</param>
    /// <param name="style">The popup styles.</param>
    /// <param name="x">The screen x coordinate.</param>
    /// <param name="y">The screen y coordinate.</param>
    /// <param name="width">The physical width.</param>
    /// <param name="height">The physical height.</param>
    /// <param name="owner">The game owner.</param>
    /// <param name="menu">The unused menu handle.</param>
    /// <param name="instance">The unused application instance.</param>
    /// <param name="parameter">The unused creation data.</param>
    /// <returns>The new handle, or zero on failure.</returns>
    [LibraryImport(_userLibrary, EntryPoint = _createWindowEntry, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(uint extended, string className, string title, uint style, int x, int y, int width, int height, nint owner, nint menu, nint instance, nint parameter);

    /// <summary>
    /// Shows or hides an overlay-owned window without activating it.
    /// </summary>
    /// <param name="window">The owned native window.</param>
    /// <param name="command">The Windows show command.</param>
    /// <returns>Whether the window was previously visible.</returns>
    [LibraryImport(_userLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint window, int command);

    /// <summary>
    /// Destroys only a renderer-owned window on its owning thread.
    /// </summary>
    /// <param name="window">The owned handle.</param>
    /// <returns>Whether destruction succeeded.</returns>
    [LibraryImport(_userLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint window);

    /// <summary>
    /// Composites the physical bitmap onto the desktop.
    /// </summary>
    /// <param name="window">The owned layered window.</param>
    /// <param name="destinationDc">The default palette context.</param>
    /// <param name="position">The physical screen position.</param>
    /// <param name="size">The physical size.</param>
    /// <param name="sourceDc">The bitmap context.</param>
    /// <param name="origin">The bitmap origin.</param>
    /// <param name="colorKey">The unused color key.</param>
    /// <param name="blend">The per-pixel alpha configuration.</param>
    /// <param name="flags">The alpha compositor flag.</param>
    /// <returns>Whether the complete update succeeded.</returns>
    [LibraryImport(_userLibrary, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateLayeredWindow(nint window, nint destinationDc, ref NativePoint position, ref NativeSize size, nint sourceDc, ref NativePoint origin, uint colorKey, ref BlendFunction blend, uint flags);

    /// <summary>
    /// Creates an owned memory device context.
    /// </summary>
    /// <param name="context">Zero for the default display compatibility.</param>
    /// <returns>The context, or zero on failure.</returns>
    [LibraryImport(_gdiLibrary, SetLastError = true)]
    internal static partial nint CreateCompatibleDC(nint context);

    /// <summary>
    /// Allocates a directly writable physical pixel buffer.
    /// </summary>
    /// <param name="context">The owned memory context.</param>
    /// <param name="info">The top-down bitmap header.</param>
    /// <param name="usage">The RGB color mode.</param>
    /// <param name="pixels">The writable BGRA memory address.</param>
    /// <param name="section">The unused shared-memory handle.</param>
    /// <param name="offset">The unused shared-memory offset.</param>
    /// <returns>The bitmap handle, or zero on failure.</returns>
    [LibraryImport(_gdiLibrary, SetLastError = true)]
    internal static partial nint CreateDIBSection(nint context, ref BitmapInfo info, uint usage, out nint pixels, nint section, uint offset);

    /// <summary>
    /// Selects or restores the bitmap in an owned memory context.
    /// </summary>
    /// <param name="context">The owned context.</param>
    /// <param name="value">The bitmap to select.</param>
    /// <returns>The previously selected object.</returns>
    [LibraryImport(_gdiLibrary)]
    internal static partial nint SelectObject(nint context, nint value);

    /// <summary>
    /// Releases an unselected bitmap.
    /// </summary>
    /// <param name="value">The owned GDI object.</param>
    /// <returns>Whether release succeeded.</returns>
    [LibraryImport(_gdiLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint value);

    /// <summary>
    /// Releases an owned memory device context.
    /// </summary>
    /// <param name="context">The owned context.</param>
    /// <returns>Whether release succeeded.</returns>
    [LibraryImport(_gdiLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(nint context);

    /// <summary>
    /// Retrieves the current native thread identity for bounded queue wakeups.
    /// </summary>
    /// <returns>The current Windows thread identity.</returns>
    [LibraryImport(_kernelLibrary)]
    internal static partial uint GetCurrentThreadId();

    /// <summary>
    /// Enqueues a wakeup on an already initialized renderer queue.
    /// </summary>
    /// <param name="thread">The renderer thread.</param>
    /// <param name="message">The wakeup message.</param>
    /// <param name="wParam">The message argument.</param>
    /// <param name="lParam">The message argument.</param>
    /// <returns>Whether posting succeeded.</returns>
    [LibraryImport(_userLibrary, EntryPoint = _postMessageEntry, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);

    /// <summary>
    /// Waits for work or a window message without polling an idle renderer.
    /// </summary>
    /// <param name="message">The next message.</param>
    /// <param name="window">Zero for all thread-owned windows.</param>
    /// <param name="minimum">The first message filter.</param>
    /// <param name="maximum">The last message filter.</param>
    /// <returns>A positive value for a message, zero for quit, or minus one on error.</returns>
    [LibraryImport(_userLibrary, EntryPoint = _getMessageEntry, SetLastError = true)]
    internal static partial int GetMessage(out Message message, nint window, uint minimum, uint maximum);

    /// <summary>
    /// Ensures a queue exists without removing a pending message.
    /// </summary>
    /// <param name="message">The optional pending message.</param>
    /// <param name="window">The window filter.</param>
    /// <param name="minimum">The first filter.</param>
    /// <param name="maximum">The last filter.</param>
    /// <param name="remove">The non-removing lookup mode.</param>
    /// <returns>Whether a message is waiting.</returns>
    [LibraryImport(_userLibrary, EntryPoint = _peekMessageEntry)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessage(out Message message, nint window, uint minimum, uint maximum, uint remove);

    /// <summary>
    /// Dispatches system messages to their native owned window.
    /// </summary>
    /// <param name="message">The current message.</param>
    /// <returns>The native window-procedure result.</returns>
    [LibraryImport(_userLibrary, EntryPoint = _dispatchMessageEntry)]
    internal static partial nint DispatchMessage(ref Message message);

    /// <summary>
    /// Starts an alignment timer only while a presentation is active.
    /// </summary>
    /// <param name="window">Zero for a renderer-thread timer.</param>
    /// <param name="identity">The timer identity.</param>
    /// <param name="milliseconds">The alignment interval in milliseconds.</param>
    /// <param name="callback">Zero to deliver timer messages.</param>
    /// <returns>The timer identity, or zero on failure.</returns>
    [LibraryImport(_userLibrary, SetLastError = true)]
    internal static partial nuint SetTimer(nint window, nuint identity, uint milliseconds, nint callback);

    /// <summary>
    /// Stops an alignment timer on its owning thread.
    /// </summary>
    /// <param name="window">The thread-timer window value.</param>
    /// <param name="identity">The active timer identity.</param>
    /// <returns>Whether removal succeeded.</returns>
    [LibraryImport(_userLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool KillTimer(nint window, nuint identity);

    /// <summary>
    /// Selects physical per-monitor coordinates only for the renderer-owned thread.
    /// </summary>
    /// <param name="context">The per-monitor-v2 context.</param>
    /// <returns>The previous thread context.</returns>
    [LibraryImport(_userLibrary, SetLastError = true)]
    private static partial nint SetThreadDpiAwarenessContext(nint context);
}
