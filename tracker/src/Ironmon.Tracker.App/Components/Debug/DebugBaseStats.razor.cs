using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Renders final original and generated base stats for one inspected Pokemon.
/// </summary>
public partial class DebugBaseStats
{
    /// <summary>
    /// Gets or sets the game-owned inspector snapshot.
    /// </summary>
    [Parameter]
    public DebugPokemonInspectorSnapshot Pokemon { get; set; } = null!;
}
