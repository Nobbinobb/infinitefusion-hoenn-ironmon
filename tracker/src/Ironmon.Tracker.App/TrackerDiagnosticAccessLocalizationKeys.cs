namespace Ironmon.Tracker.App;

/// <summary>
/// Creates stable resource keys for diagnostic capability presentation in the tracker.
/// </summary>
public static class TrackerDiagnosticAccessLocalizationKeys
{
    private const string _capabilityPrefix = "Access.Capabilities.";
    private const string _nameSuffix = ".Name";

    /// <summary>
    /// Gets the localized-name resource key for one diagnostic capability.
    /// </summary>
    /// <param name="capability">The stable capability identifier.</param>
    /// <returns>The capability-name resource key.</returns>
    public static string GetCapabilityName(string capability)
        => _capabilityPrefix + capability + _nameSuffix;
}
