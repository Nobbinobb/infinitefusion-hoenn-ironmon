namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents the complete requirements for the focused graph branch outside the transformed canvas.
/// </summary>
public partial class EvolutionGraph
{
    private bool _routesOpen;
    private readonly HashSet<string> _relatedSpeciesIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the focused node, retaining the original selection while a neighborhood loads.
    /// </summary>
    private EvolutionTargetSnapshot FocusedNode => _nodeLookup.GetValueOrDefault(FocusedSpeciesId ?? Current.SpeciesId) ?? Current;

    /// <summary>
    /// Gets whether a node shares a direct relationship with the focused branch.
    /// </summary>
    /// <param name="speciesId">The candidate node identifier.</param>
    /// <returns>Whether a direct incoming or outgoing relationship exists.</returns>
    private bool IsRelated(string speciesId)
        => _relatedSpeciesIds.Contains(speciesId);

    /// <summary>
    /// Collects direct focus relationships once per update for constant-time node styling.
    /// </summary>
    private void UpdateRelatedSpecies()
    {
        _relatedSpeciesIds.Clear();
        foreach (EvolutionGraphEdge edge in Edges.Where(IsActive))
        {
            _relatedSpeciesIds.Add(edge.SourceSpeciesId);
            _relatedSpeciesIds.Add(edge.TargetSpeciesId);
        }
    }

    /// <summary>
    /// Gets incoming or outgoing routes, including nodes hidden behind compact BST ranges.
    /// </summary>
    /// <param name="incoming">Whether predecessor routes should be returned.</param>
    /// <returns>The loaded routes with available endpoint snapshots.</returns>
    private IReadOnlyList<EvolutionGraphEdge> GetFocusedRoutes(bool incoming)
    {
        return [.. Edges.Where(edge => incoming ? IsFocused(edge.TargetSpeciesId) : IsFocused(edge.SourceSpeciesId))
            .Where(edge => _nodeLookup.ContainsKey(incoming ? edge.SourceSpeciesId : edge.TargetSpeciesId))];
    }

    /// <summary>
    /// Opens the focused branch requirements.
    /// </summary>
    private void OpenRoutes()
        => _routesOpen = true;

    /// <summary>
    /// Closes the branch requirements without navigating the canvas.
    /// </summary>
    private void CloseRoutes()
        => _routesOpen = false;

    /// <summary>
    /// Closes the route dialog and focuses the selected endpoint.
    /// </summary>
    /// <param name="speciesId">The related node to focus.</param>
    /// <returns>A task representing selection and any required neighborhood loading.</returns>
    private async Task SelectRouteAsync(string speciesId)
    {
        _routesOpen = false;
        if (Focused.HasDelegate)
        {
            await Focused.InvokeAsync(speciesId);
        }
        else
        {
            await Selected.InvokeAsync(speciesId);
        }
    }
}
