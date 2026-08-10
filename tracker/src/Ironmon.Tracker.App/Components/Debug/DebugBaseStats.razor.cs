using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Renders final original and generated base stats for one inspected Pokemon.
/// </summary>
public partial class DebugBaseStats
{
    /// <summary>
    /// Gets or sets the game-owned stats snapshot.
    /// </summary>
    [Parameter]
    public DebugPokemonStatsSnapshot Stats { get; set; } = null!;

    /// <summary>
    /// Gets or sets whether the inspected Pokemon is a fusion.
    /// </summary>
    [Parameter]
    public bool Fusion { get; set; }
}
