namespace Ironmon.Tracker.AccessGenerator.App.Components;

/// <summary>
/// Describes one localized capability group shown by the generator.
/// </summary>
/// <param name="resourceKey">The localized group-name resource key.</param>
/// <param name="capabilities">The capability identifiers in display order.</param>
/// <exception cref="ArgumentException">Thrown when resourceKey is empty.</exception>
/// <exception cref="ArgumentNullException">Thrown when capabilities is null.</exception>
/// <remarks>Initializes an immutable generator capability group.</remarks>
public sealed class GeneratorCapabilityGroup(string resourceKey, IReadOnlyList<string> capabilities)
{
    /// <summary>
    /// Gets the localized group-name resource key.
    /// </summary>
    public string ResourceKey { get; } = string.IsNullOrWhiteSpace(resourceKey) ? throw new ArgumentException("A group resource key is required.", nameof(resourceKey)) : resourceKey;

    /// <summary>
    /// Gets the capability identifiers in display order.
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; } = [.. capabilities ?? throw new ArgumentNullException(nameof(capabilities))];
}
