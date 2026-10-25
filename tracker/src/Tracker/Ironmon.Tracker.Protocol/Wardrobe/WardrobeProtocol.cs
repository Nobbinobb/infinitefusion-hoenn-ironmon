namespace Ironmon.Tracker.Protocol.Wardrobe;

/// <summary>
/// Defines the game-only wardrobe protocol and native appearance identifiers.
/// </summary>
public static class WardrobeProtocol
{
    /// <summary>
    /// Gets the supported wardrobe protocol version.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// Gets the maximum accepted preview PNG size in bytes.
    /// </summary>
    public const int MaximumPngBytes = 131_072;

    /// <summary>
    /// Gets the game-owned wardrobe event name.
    /// </summary>
    public const string StateEvent = "wardrobe_state";

    /// <summary>
    /// Gets the recovery-only state command.
    /// </summary>
    public const string StateCommand = "wardrobe_state";

    /// <summary>
    /// Gets the session-bound edit command.
    /// </summary>
    public const string ActionCommand = "wardrobe_action";

    /// <summary>
    /// Gets the native preview command.
    /// </summary>
    public const string PreviewCommand = "wardrobe_preview";

    /// <summary>
    /// Gets the native outfit category.
    /// </summary>
    public const string Clothes = "clothes";

    /// <summary>
    /// Gets the native hairstyle category.
    /// </summary>
    public const string Hair = "hair";

    /// <summary>
    /// Gets the top accessory field and category.
    /// </summary>
    public const string Hat = "hat";

    /// <summary>
    /// Gets the underneath accessory field.
    /// </summary>
    public const string Hat2 = "hat2";

    /// <summary>
    /// Gets the portrait preview mode.
    /// </summary>
    public const string Portrait = "portrait";

    /// <summary>
    /// Gets the front preview mode.
    /// </summary>
    public const string Front = "front";

    /// <summary>
    /// Gets the back preview mode.
    /// </summary>
    public const string Back = "back";

    /// <summary>
    /// Gets the left preview mode.
    /// </summary>
    public const string Left = "left";

    /// <summary>
    /// Gets the right preview mode.
    /// </summary>
    public const string Right = "right";

    /// <summary>
    /// Gets the bicycle preview mode.
    /// </summary>
    public const string Bike = "bike";

    /// <summary>
    /// Gets the hair hue field.
    /// </summary>
    public const string HairColor = "hair_color";

    /// <summary>
    /// Gets the outfit hue field.
    /// </summary>
    public const string ClothesColor = "clothes_color";

    /// <summary>
    /// Gets the top accessory hue field.
    /// </summary>
    public const string HatColor = "hat_color";

    /// <summary>
    /// Gets the underneath accessory hue field.
    /// </summary>
    public const string Hat2Color = "hat2_color";

    /// <summary>
    /// Gets the bicycle hue field.
    /// </summary>
    public const string BikeColor = "bike_color";

    /// <summary>
    /// Gets the PNG image data prefix.
    /// </summary>
    public const string PngDataPrefix = "data:image/png;base64,";
}
