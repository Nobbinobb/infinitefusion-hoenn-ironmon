using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Presents generated evolution destinations for the currently inspected Pokemon.
/// </summary>
public partial class DebugEvolutionTargets
{
    /// <summary>
    /// Gets or sets the game-owned inspector snapshot.
    /// </summary>
    [Parameter]
    public DebugPokemonInspectorSnapshot Pokemon { get; set; } = null!;

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a generated destination is selected.
    /// </summary>
    [Parameter]
    public EventCallback<string> Selected { get; set; }

    /// <summary>
    /// Gets whether the inspected Pokemon has any generated destination.
    /// </summary>
    /// <returns>Whether at least one target list is populated.</returns>
    private bool HasTargets()
        => Pokemon.EvolutionTargets.Count + Pokemon.HeadEvolutionTargets.Count + Pokemon.BodyEvolutionTargets.Count > 0;

    /// <summary>
    /// Gets whether the inspected Pokemon has any generated graph edge.
    /// </summary>
    /// <returns>Whether the evolution graph should be displayed.</returns>
    private bool HasGraph()
        => Pokemon.EvolutionPredecessors.Count + Pokemon.EvolutionTargets.Count + Pokemon.HeadEvolutionTargets.Count + Pokemon.BodyEvolutionTargets.Count > 0;

    /// <summary>
    /// Creates the inspected Pokemon's current graph node.
    /// </summary>
    /// <returns>The current generated graph node.</returns>
    private EvolutionTargetSnapshot GetCurrentEvolutionNode()
        => new()
        {
            SpeciesId = Pokemon.SpeciesId,
            SpeciesName = Pokemon.SpeciesName,
            SpritePath = Pokemon.SpritePath,
            BaseStatTotal = Pokemon.GeneratedBaseStatTotal
        };
}
