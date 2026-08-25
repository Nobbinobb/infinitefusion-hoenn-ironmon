using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Renders one navigable generated evolution target.
/// </summary>
public partial class EvolutionTargetButton
{
    /// <summary>
    /// Gets or sets the generated destination.
    /// </summary>
    [Parameter]
    public EvolutionTargetSnapshot Target { get; set; } = null!;

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the destination is selected.
    /// </summary>
    [Parameter]
    public EventCallback<string> Selected { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the node is focused without navigation.
    /// </summary>
    [Parameter]
    public EventCallback<string> Focused { get; set; }

    /// <summary>
    /// Gets or sets whether this node is the currently focused branch.
    /// </summary>
    [Parameter]
    public bool IsFocused { get; set; }

    /// <summary>
    /// Navigates to the generated destination.
    /// </summary>
    /// <returns>A task representing the callback.</returns>
    private Task SelectAsync()
        => Selected.InvokeAsync(Target.SpeciesId);

    /// <summary>
    /// Focuses the node when branch selection is available, or otherwise navigates to it.
    /// </summary>
    /// <returns>A task representing the callback.</returns>
    private Task ActivateAsync()
        => Focused.HasDelegate ? Focused.InvokeAsync(Target.SpeciesId) : SelectAsync();

    /// <summary>
    /// Gets the focus action tooltip when the node has separate focus and navigation actions.
    /// </summary>
    /// <returns>The localized tooltip, or null for a directly navigable node.</returns>
    private string? GetActionTitle()
        => Focused.HasDelegate ? Text["Lookup.Graph.SelectNode"].Value : null;
}
