using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Renders game-owned run and randomizer diagnostics.
/// </summary>
public partial class DebugRunDiagnostics
{
    /// <summary>
    /// Gets or sets the current authorized diagnostics snapshot.
    /// </summary>
    [Parameter]
    public DebugRunDiagnosticsSnapshot Diagnostics { get; set; } = null!;
}
