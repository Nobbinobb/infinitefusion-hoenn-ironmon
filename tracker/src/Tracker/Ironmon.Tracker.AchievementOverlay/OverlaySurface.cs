using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Ironmon.Tracker.AchievementOverlay;

/// <summary>
/// Owns one directly writable premultiplied-alpha DIB and its memory context.
/// </summary>
internal sealed class OverlaySurface : IDisposable
{
    private nint _bitmap;
    private nint _previous;

    /// <summary>
    /// Allocates the native surface at the actual output size.
    /// </summary>
    /// <param name="width">The physical width in pixels.</param>
    /// <param name="height">The physical height in pixels.</param>
    internal OverlaySurface(int width, int height)
    {
        Context = OverlayNative.CreateCompatibleDC(0);
        if (Context == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        try
        {
            OverlayNative.BitmapInfo info = new() { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            _bitmap = OverlayNative.CreateDIBSection(Context, ref info, 0, out nint pixels, 0, 0);
            if (_bitmap == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            _previous = OverlayNative.SelectObject(Context, _bitmap);
            if (_previous is 0 or -1)
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            Bitmap = new(width, height, checked(width * 4), PixelFormat.Format32bppPArgb, pixels);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the memory context uploaded directly to the desktop compositor.
    /// </summary>
    internal nint Context { get; private set; }

    /// <summary>
    /// Gets the drawing view over the same native pixels, without a second frame copy.
    /// </summary>
    internal Bitmap Bitmap { get; } = null!;

    /// <summary>
    /// Releases the managed drawing view before freeing the native pixel buffer.
    /// </summary>
    public void Dispose()
    {
        if (Context == 0)
            return;

        Bitmap?.Dispose();
        if (_previous is not (0 or -1))
            OverlayNative.SelectObject(Context, _previous);

        if (_bitmap != 0)
            OverlayNative.DeleteObject(_bitmap);

        OverlayNative.DeleteDC(Context);
        Context = 0;
        _bitmap = 0;
        _previous = 0;
    }
}
