using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Presents generated Pokémon lookup for the active debug run.
/// </summary>
public partial class DebugActiveRunLookup
{
    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }
}
