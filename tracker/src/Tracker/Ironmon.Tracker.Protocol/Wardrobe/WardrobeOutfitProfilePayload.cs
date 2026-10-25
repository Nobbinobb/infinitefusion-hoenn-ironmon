namespace Ironmon.Tracker.Protocol.Wardrobe;

/// <summary>
/// Describes a named shared outfit without storing skin tone as part of the outfit.
/// </summary>
public sealed record WardrobeOutfitProfilePayload
{
    /// <summary>
    /// Gets the stable saved-outfit identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the user-assigned name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the saved pieces and dyes with the current character skin tone.
    /// </summary>
    public WardrobeAppearancePayload Appearance { get; init; } = new();

    /// <summary>
    /// Gets whether all saved pieces are currently installed and permanently owned.
    /// </summary>
    public bool Available { get; init; }
}
