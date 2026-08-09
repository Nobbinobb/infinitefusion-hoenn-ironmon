namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Defines supported local sprite formats and data-URL values.
/// </summary>
internal static class LocalSpriteConstants
{
    /// <summary>
    /// Gets the GIF file extension.
    /// </summary>
    internal const string GifExtension = ".gif";

    /// <summary>
    /// Gets the GIF media type.
    /// </summary>
    internal const string GifMediaType = "image/gif";

    /// <summary>
    /// Gets the PNG media type used for non-GIF game sprites.
    /// </summary>
    internal const string PngMediaType = "image/png";

    /// <summary>
    /// Gets the data-URL base64 marker.
    /// </summary>
    internal const string Base64Marker = "base64";
}
