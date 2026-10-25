using System.Drawing;

namespace Ironmon.Tracker.AchievementOverlay;

/// <summary>
/// Hosts the physical-resolution notification in a native layered window without WPF or another WebView.
/// </summary>
internal sealed class OverlayWindow : IDisposable
{
    private OverlaySurface? _surface;
    private OverlayArtwork? _artwork;
    private OverlayPlacement? _placement;
    private int _offset = int.MinValue;
    private bool _original;

    /// <summary>
    /// Creates a hidden, click-through, nonactivating window owned by the intended game.
    /// </summary>
    /// <param name="gameWindow">The selected game's native handle.</param>
    internal OverlayWindow(nint gameWindow) => Handle = OverlayNative.CreateWindow(gameWindow);

    /// <summary>
    /// Gets the overlay-owned native handle, or zero after disposal.
    /// </summary>
    internal nint Handle { get; private set; }

    /// <summary>
    /// Gets whether the native window is currently shown.
    /// </summary>
    internal bool IsVisible { get; private set; }

    /// <summary>
    /// Gets the actual submitted pixel buffer for native renderer acceptance.
    /// </summary>
    internal Bitmap Frame => _surface?.Bitmap ?? throw new InvalidOperationException("The overlay has no rendered frame.");

    /// <summary>
    /// Repaints only changed animation pixels and follows the game without changing focus.
    /// </summary>
    /// <param name="artwork">The approved icon and title.</param>
    /// <param name="placement">The physical bounds.</param>
    /// <param name="original">Whether to compare the small source at larger sizes.</param>
    /// <param name="fraction">The visible slide fraction between zero and one.</param>
    internal void ShowAligned(OverlayArtwork artwork, OverlayPlacement placement, bool original, double fraction = 1)
    {
        ObjectDisposedException.ThrowIf(Handle == 0, this);
        int offset = (int)Math.Round((-340 + 348 * Math.Clamp(fraction, 0, 1)) * placement.Scale);
        bool redraw = !ReferenceEquals(_artwork, artwork) || _placement?.Scale != placement.Scale || _placement.Width != placement.Width || _placement.Height != placement.Height || _offset != offset || _original != original;
        if (redraw)
        {
            if (_surface is null || _surface.Bitmap.Width != placement.Width || _surface.Bitmap.Height != placement.Height)
            {
                _surface?.Dispose();
                _surface = null;
                _surface = new(placement.Width, placement.Height);
            }

            OverlayCanvas.Draw(_surface.Bitmap, artwork, placement, original, offset);
        }

        if (redraw || _placement != placement || !IsVisible)
            OverlayNative.Present(Handle, _surface!.Context, placement);

        _artwork = artwork;
        _placement = placement;
        _offset = offset;
        _original = original;
        IsVisible = true;
    }

    /// <summary>
    /// Hides the native window without advancing or discarding its game-clocked phase.
    /// </summary>
    internal void Hide()
    {
        if (Handle != 0 && IsVisible)
            OverlayNative.ShowWindow(Handle, 0);

        IsVisible = false;
    }

    /// <summary>
    /// Destroys only the overlay-owned window and its physical pixel surface.
    /// </summary>
    public void Dispose()
    {
        if (Handle == 0)
            return;

        OverlayNative.DestroyWindow(Handle);
        Handle = 0;
        IsVisible = false;
        _surface?.Dispose();
        _surface = null;
        _artwork = null;
    }
}
