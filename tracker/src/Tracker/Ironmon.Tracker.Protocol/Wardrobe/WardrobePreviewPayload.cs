namespace Ironmon.Tracker.Protocol.Wardrobe;

/// <summary>
/// Contains a native-rendered PNG and exact engine-derived cosmetic color swatches.
/// </summary>
public sealed record WardrobePreviewPayload
{
    /// <summary>
    /// Gets the originating editing session.
    /// </summary>
    public string WardrobeId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the appearance revision used for this preview.
    /// </summary>
    public long Revision { get; init; }

    /// <summary>
    /// Gets the bounded PNG bytes encoded as base64.
    /// </summary>
    public string PngBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the native cropped image width in pixels.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Gets the native cropped image height in pixels.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Gets representative colors for the installed native hair variants.
    /// </summary>
    public IReadOnlyDictionary<int,string> BaseColors { get; init; } = new Dictionary<int,string>();

    /// <summary>
    /// Gets the 36 native hue choices, in ten-degree increments.
    /// </summary>
    public IReadOnlyList<string> DyeColors { get; init; } = [];

    /// <summary>
    /// Gets representative colors for the native skin tones.
    /// </summary>
    public IReadOnlyDictionary<int,string> SkinColors { get; init; } = new Dictionary<int,string>();
}
