using System.Text.Json.Serialization;

namespace Ironmon.Tracker.Protocol.Wardrobe;

/// <summary>
/// Describes a complete native cosmetic appearance, without changing ownership.
/// </summary>
public sealed record WardrobeAppearancePayload
{
    /// <summary>
    /// Gets the installed outfit identifier.
    /// </summary>
    public string Clothes { get; init; } = string.Empty;

    /// <summary>
    /// Gets the native base-variant and hairstyle identifier.
    /// </summary>
    public string Hair { get; init; } = string.Empty;

    /// <summary>
    /// Gets the top accessory identifier, or null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Hat { get; init; }

    /// <summary>
    /// Gets the underneath accessory identifier, or null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Hat2 { get; init; }

    /// <summary>
    /// Gets the native skin-tone index from one through six.
    /// </summary>
    public int SkinTone { get; init; } = 1;

    /// <summary>
    /// Gets the hair hue rotation from zero through 359 degrees.
    /// </summary>
    public int HairColor { get; init; }

    /// <summary>
    /// Gets the outfit hue rotation from zero through 359 degrees.
    /// </summary>
    public int ClothesColor { get; init; }

    /// <summary>
    /// Gets the top accessory hue rotation from zero through 359 degrees.
    /// </summary>
    public int HatColor { get; init; }

    /// <summary>
    /// Gets the underneath accessory hue rotation from zero through 359 degrees.
    /// </summary>
    public int Hat2Color { get; init; }

    /// <summary>
    /// Gets the bicycle hue rotation from zero through 359 degrees.
    /// </summary>
    public int BikeColor { get; init; }
}
