using System.Drawing;
using System.IO;
using Ironmon.Tracker.Connection.Transport;

namespace Ironmon.Tracker.AchievementOverlay;

/// <summary>
/// Owns only the current notification's decoded images without a UI framework or persistent files.
/// </summary>
/// <remarks>
/// Initializes the approved title and stream-independent image pair.
/// </remarks>
/// <param name="Title">The approved title.</param>
/// <param name="Small">The approved small image.</param>
/// <param name="Detail">The optional high-resolution source.</param>
internal sealed record OverlayArtwork(string Title, Bitmap Small, Bitmap? Detail) : IDisposable
{
    /// <summary>
    /// Decodes one authorized presentation and releases partial results on failure.
    /// </summary>
    /// <param name="presentation">The authorized PNG buffers.</param>
    /// <returns>The owned image pair.</returns>
    internal static OverlayArtwork FromPresentation(AchievementOverlayPresentation presentation)
    {
        Bitmap small = Decode(presentation.SmallPng);
        try
        {
            return new(presentation.Title, small, presentation.DetailPng is null ? null : Decode(presentation.DetailPng));
        }
        catch
        {
            small.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Chooses enough source pixels for the physical display size.
    /// </summary>
    /// <param name="iconPixels">The physical icon width and height.</param>
    /// <param name="useOriginal">Whether to compare the small source at larger scales.</param>
    /// <returns>The selected decoded source.</returns>
    internal Bitmap Select(int iconPixels, bool useOriginal) => useOriginal || iconPixels <= 50 || Detail is null ? Small : Detail;

    /// <summary>
    /// Releases both decoded sources when the presentation ends.
    /// </summary>
    public void Dispose()
    {
        Small.Dispose();
        Detail?.Dispose();
    }

    /// <summary>
    /// Detaches decoded pixels from the temporary PNG stream.
    /// </summary>
    /// <param name="png">The authenticated PNG bytes.</param>
    /// <returns>A stream-independent bitmap.</returns>
    private static Bitmap Decode(byte[] png)
    {
        using MemoryStream stream = new(png, false);
        using Image source = Image.FromStream(stream, false, true);
        return new Bitmap(source);
    }
}
