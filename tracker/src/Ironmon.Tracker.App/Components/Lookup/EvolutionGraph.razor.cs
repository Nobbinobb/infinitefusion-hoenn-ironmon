using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Renders a clickable one-step generated evolution graph.
/// </summary>
public partial class EvolutionGraph
{
    /// <summary>
    /// Gets or sets the selected graph node.
    /// </summary>
    [Parameter]
    public EvolutionTargetSnapshot Current { get; set; } = null!;

    /// <summary>
    /// Gets or sets immediate generated normal predecessors.
    /// </summary>
    [Parameter]
    public IReadOnlyList<EvolutionTargetSnapshot> Predecessors { get; set; } = [];

    /// <summary>
    /// Gets or sets immediate generated normal targets.
    /// </summary>
    [Parameter]
    public IReadOnlyList<EvolutionTargetSnapshot> Targets { get; set; } = [];

    /// <summary>
    /// Gets or sets immediate targets activated by the fusion Head.
    /// </summary>
    [Parameter]
    public IReadOnlyList<EvolutionTargetSnapshot> HeadTargets { get; set; } = [];

    /// <summary>
    /// Gets or sets immediate targets activated by the fusion Body.
    /// </summary>
    [Parameter]
    public IReadOnlyList<EvolutionTargetSnapshot> BodyTargets { get; set; } = [];

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a graph node is selected.
    /// </summary>
    [Parameter]
    public EventCallback<string> Selected { get; set; }

    /// <summary>
    /// Gets whether the graph contains any outgoing edge.
    /// </summary>
    /// <returns>Whether at least one outgoing target exists.</returns>
    private bool HasOutgoingTargets()
        => Targets.Count + HeadTargets.Count + BodyTargets.Count > 0;
}
