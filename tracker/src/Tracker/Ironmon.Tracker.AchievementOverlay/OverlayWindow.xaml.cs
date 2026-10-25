using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Ironmon.Tracker.AchievementOverlay;

/// <summary>
/// Renders the one-icon notification directly at desktop resolution above the game's enlarged frame.
/// </summary>
internal sealed partial class OverlayWindow : Window
{
    private readonly nint _handle;

    /// <summary>
    /// Creates a hidden, click-through, nonactivating prototype window owned by the selected game.
    /// </summary>
    /// <param name="gameWindow">The selected game window owning this notification.</param>
    internal OverlayWindow(nint gameWindow)
    {
        InitializeComponent();
        WindowInteropHelper helper = new(this) { Owner = gameWindow };
        _handle = helper.EnsureHandle();
        NativeGameWindow.ConfigureOverlay(_handle);
    }

    /// <summary>
    /// Gets the native handle for nonactivation and physical-size acceptance checks.
    /// </summary>
    internal nint Handle => _handle;

    /// <summary>
    /// Updates the image source and follows the game using physical pixel bounds.
    /// </summary>
    /// <param name="artwork">The one approved icon and title.</param>
    /// <param name="placement">The current physical notification bounds.</param>
    /// <param name="original">Whether to demonstrate the original small source at all scales.</param>
    /// <param name="fraction">The visible slide fraction, from zero to one.</param>
    internal void ShowAligned(OverlayArtwork artwork, OverlayPlacement placement, bool original, double fraction = 1)
    {
        Badge.Source = artwork.Select(placement.IconPixels, original);
        RenderOptions.SetBitmapScalingMode(Badge, original ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        AchievementTitle.Text = artwork.Title;
        double offset = Math.Round((-PanelContent.Width + (PanelContent.Width + 8) * Math.Clamp(fraction, 0, 1)) * placement.Scale) / placement.Scale;
        PanelContent.RenderTransform = new TranslateTransform(offset, 0);
        if (!IsVisible)
            Show();

        NativeGameWindow.PlaceOverlay(_handle, placement);
    }
}
