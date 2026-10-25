using System.IO;
using System.Windows.Media.Imaging;
using Ironmon.Tracker.Connection.Transport;

namespace Ironmon.Tracker.AchievementOverlay;

/// <summary>
/// Keeps only the current notification's decoded images in memory.
/// </summary>
/// <remarks>
/// Initializes frozen image sources obtained from authorized embedded artwork.
/// </remarks>
/// <param name="Title">The approved title.</param>
/// <param name="Small">The exact approved small PNG.</param>
/// <param name="Detail">The optional high-resolution source.</param>
internal sealed record OverlayArtwork(string Title, BitmapSource Small, BitmapSource? Detail)
{
    /// <summary>
    /// Decodes one authorized presentation without extracting any files.
    /// </summary>
    /// <param name="presentation">The authorized PNG buffers.</param>
    /// <returns>The current notification's frozen images.</returns>
    internal static OverlayArtwork FromPresentation(AchievementOverlayPresentation presentation)
        => new(presentation.Title, Decode(presentation.SmallPng), presentation.DetailPng is null ? null : Decode(presentation.DetailPng));

    /// <summary>
    /// Chooses enough source pixels for the physical display size.
    /// </summary>
    /// <param name="iconPixels">The physical icon width and height.</param>
    /// <param name="useOriginal">Whether an acceptance check requests the small-source reference.</param>
    /// <returns>The current immutable image source.</returns>
    internal BitmapSource Select(int iconPixels, bool useOriginal) => useOriginal || iconPixels <= 50 || Detail is null ? Small : Detail;

    /// <summary>
    /// Decodes and freezes a PNG while releasing its temporary stream.
    /// </summary>
    /// <param name="png">The authenticated PNG bytes.</param>
    /// <returns>A frozen, fully loaded image.</returns>
    private static BitmapImage Decode(byte[] png)
    {
        using MemoryStream stream = new(png, false);
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
