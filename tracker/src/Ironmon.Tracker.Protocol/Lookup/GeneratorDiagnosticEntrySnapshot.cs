namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Describes one labeled value in deterministic generator diagnostics.
/// </summary>
public sealed class GeneratorDiagnosticEntrySnapshot
{
    /// <summary>
    /// Initializes an empty generator diagnostic entry for protocol serialization.
    /// </summary>
    public GeneratorDiagnosticEntrySnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the stable diagnostic key.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets or initializes the visible diagnostic value.
    /// </summary>
    public required string Value { get; init; }
}
