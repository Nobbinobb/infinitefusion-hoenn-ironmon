using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Renders the labels and direction marker for one generated evolution edge.
/// </summary>
public partial class EvolutionGraphEdge
{
    /// <summary>
    /// Gets or sets the optional evolving fusion side.
    /// </summary>
    [Parameter]
    public string? Side { get; set; }

    /// <summary>
    /// Gets or sets every effective method represented by the edge.
    /// </summary>
    [Parameter]
    public IReadOnlyList<string> Methods { get; set; } = [];
}
