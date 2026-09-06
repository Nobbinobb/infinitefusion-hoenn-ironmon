using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Renders deterministic generator metadata inside its corresponding information tab.
/// </summary>
public partial class GeneratorDiagnostics
{
    /// <summary>
    /// Gets or sets the visible generator heading.
    /// </summary>
    [Parameter]
    public required string Heading { get; set; }

    /// <summary>
    /// Gets or sets the ordered generator diagnostics.
    /// </summary>
    [Parameter]
    public GeneratorDiagnosticsSnapshot Diagnostics { get; set; } = new();

    /// <summary>
    /// Formats a stable protocol key as a compact visible label.
    /// </summary>
    /// <param name="key">The stable diagnostic key.</param>
    /// <returns>The visible label.</returns>
    private static string FormatLabel(string key)
        => key.Replace('_', ' ').ToUpperInvariant();

    /// <summary>
    /// Gets whether a long diagnostic value should span the full grid width.
    /// </summary>
    /// <param name="entry">The represented diagnostic entry.</param>
    /// <returns>Whether the entry uses the wide layout.</returns>
    private static bool IsWide(GeneratorDiagnosticEntrySnapshot entry)
        => entry.Key.Contains("fingerprint", StringComparison.Ordinal) || entry.Value.Length > 32;
}
