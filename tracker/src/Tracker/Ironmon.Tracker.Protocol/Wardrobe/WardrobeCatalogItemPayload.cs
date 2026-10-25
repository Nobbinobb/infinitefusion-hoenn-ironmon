namespace Ironmon.Tracker.Protocol.Wardrobe;

/// <summary>
/// Describes one installed wardrobe item and its game-authoritative point price.
/// </summary>
public sealed record WardrobeCatalogItemPayload
{
    /// <summary>
    /// Gets the category-qualified item key.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets the native cosmetic identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the native clothes, hair, or hat category.
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Gets the authored display name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the authored search terms.
    /// </summary>
    public string Tags { get; init; } = string.Empty;

    /// <summary>
    /// Gets the permanent unlock price in wardrobe points.
    /// </summary>
    public int Points { get; init; }

    /// <summary>
    /// Gets whether the required native assets are installed.
    /// </summary>
    public bool Available { get; init; }

    /// <summary>
    /// Gets the installed base-color variants for a hairstyle.
    /// </summary>
    public IReadOnlyList<int> Variants { get; init; } = [];
}
