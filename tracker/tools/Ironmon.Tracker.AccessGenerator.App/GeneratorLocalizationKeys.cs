namespace Ironmon.Tracker.AccessGenerator.App;

/// <summary>
/// Creates stable resource keys for identifier-backed generator presentation.
/// </summary>
public static class GeneratorLocalizationKeys
{
    private const string CapabilityPrefix = "Generator.Capabilities.";
    private const string PresetPrefix = "Generator.Presets.";
    private const string NameSuffix = ".Name";
    private const string DescriptionSuffix = ".Description";

    /// <summary>
    /// Gets the localized-name resource key for one diagnostic capability.
    /// </summary>
    /// <param name="capability">The stable capability identifier.</param>
    /// <returns>The capability-name resource key.</returns>
    public static string GetCapabilityName(string capability)
        => CapabilityPrefix + capability + NameSuffix;

    /// <summary>
    /// Gets the localized-description resource key for one diagnostic capability.
    /// </summary>
    /// <param name="capability">The stable capability identifier.</param>
    /// <returns>The capability-description resource key.</returns>
    public static string GetCapabilityDescription(string capability)
        => CapabilityPrefix + capability + DescriptionSuffix;

    /// <summary>
    /// Gets the localized-name resource key for one local preset.
    /// </summary>
    /// <param name="presetId">The stable local preset identifier.</param>
    /// <returns>The preset-name resource key.</returns>
    public static string GetPresetName(string presetId)
        => PresetPrefix + presetId + NameSuffix;

    /// <summary>
    /// Gets the localized-description resource key for one local preset.
    /// </summary>
    /// <param name="presetId">The stable local preset identifier.</param>
    /// <returns>The preset-description resource key.</returns>
    public static string GetPresetDescription(string presetId)
        => PresetPrefix + presetId + DescriptionSuffix;
}
