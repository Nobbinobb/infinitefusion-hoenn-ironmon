using Ironmon.Tracker.App.Components.Common;

namespace Ironmon.Tracker.AccessGenerator.App.Components;

/// <summary>
/// Describes one localized capability group shown by the generator.
/// </summary>
/// <param name="resourceKey">The localized group-name resource key.</param>
/// <param name="capabilities">The capability identifiers in display order.</param>
/// <param name="icon">The shared outline icon for this group.</param>
/// <exception cref="ArgumentException">Thrown when resourceKey is empty.</exception>
/// <exception cref="ArgumentNullException">Thrown when capabilities is null.</exception>
/// <remarks>Initializes an immutable generator capability group.</remarks>
public sealed class GeneratorCapabilityGroup(string resourceKey, IReadOnlyList<string> capabilities, ObsidianIconKind icon)
{
    private const string _shortNameSuffix = ".ShortName";
    private const string _descriptionSuffix = ".Description";

    /// <summary>
    /// Gets the outline icon used in the group navigation.
    /// </summary>
    public ObsidianIconKind Icon { get; } = icon;

    /// <summary>
    /// Gets the compact navigation label resource key.
    /// </summary>
    public string ShortNameKey => ResourceKey + _shortNameSuffix;

    /// <summary>
    /// Gets the group introduction resource key.
    /// </summary>
    public string DescriptionKey => ResourceKey + _descriptionSuffix;

    /// <summary>
    /// Gets the localized group-name resource key.
    /// </summary>
    public string ResourceKey { get; } = string.IsNullOrWhiteSpace(resourceKey) ? throw new ArgumentException("A group resource key is required.", nameof(resourceKey)) : resourceKey;

    /// <summary>
    /// Gets the capability identifiers in display order.
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; } = [.. capabilities ?? throw new ArgumentNullException(nameof(capabilities))];
}
