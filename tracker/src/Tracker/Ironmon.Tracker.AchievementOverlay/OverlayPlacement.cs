namespace Ironmon.Tracker.AchievementOverlay;

/// <summary>
/// Describes a notification rectangle in physical desktop pixels after the game's own scaling.
/// </summary>
/// <remarks>
/// Initializes the screen position, panel dimensions, and actual displayed icon size.
/// </remarks>
/// <param name="X">The physical desktop x coordinate.</param>
/// <param name="Y">The physical desktop y coordinate.</param>
/// <param name="Width">The panel width in physical pixels.</param>
/// <param name="Height">The panel height in physical pixels.</param>
/// <param name="IconPixels">The icon width and height in physical pixels.</param>
/// <param name="Scale">The game viewport scale relative to 512 by 384.</param>
internal sealed record OverlayPlacement(int X, int Y, int Width, int Height, int IconPixels, double Scale)
{
    /// <summary>
    /// Fits the notification to the game's aspect-preserving viewport, including letterboxing.
    /// </summary>
    /// <param name="clientX">The physical screen x coordinate of the game client area.</param>
    /// <param name="clientY">The physical screen y coordinate of the game client area.</param>
    /// <param name="clientWidth">The physical client width, excluding the title bar.</param>
    /// <param name="clientHeight">The physical client height, excluding the title bar.</param>
    /// <returns>The panel geometry matching the current game size.</returns>
    internal static OverlayPlacement FromClient(int clientX, int clientY, int clientWidth, int clientHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clientWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clientHeight);
        double scale = Math.Min(clientWidth / 512d, clientHeight / 384d);
        int x = clientX + (int)Math.Round((clientWidth - 512 * scale) / 2);
        int y = clientY + (int)Math.Round((clientHeight - 384 * scale) / 2 + 8 * scale);
        return new(x, y, Math.Max(1, (int)Math.Round(348 * scale)), Math.Max(1, (int)Math.Round(64 * scale)), Math.Max(1, (int)Math.Round(50 * scale)), scale);
    }
}
