namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Describes deterministic generator metadata for one shared information tab.
/// </summary>
public sealed class GeneratorDiagnosticsSnapshot
{
    /// <summary>
    /// Initializes empty generator diagnostics for protocol serialization.
    /// </summary>
    public GeneratorDiagnosticsSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes whether the generator is enabled for the represented run.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets or initializes the ordered diagnostic values.
    /// </summary>
    public IReadOnlyList<GeneratorDiagnosticEntrySnapshot> Entries { get; init; } = [];
}
