using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Renders a navigable Pokemon relationship with its local sprite.
/// </summary>
public partial class PokemonRelationButton
{
    /// <summary>
    /// Gets or sets whether this content uses the redesigned research presentation.
    /// </summary>
    [CascadingParameter(Name = nameof(PokemonLookupCard.ResearchRedesigned))]
    public bool Redesigned { get; set; }

    /// <summary>
    /// Gets or sets the related Pokemon.
    /// </summary>
    [Parameter]
    public PokemonRelationSnapshot Relation { get; set; } = null!;

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the related Pokemon is selected.
    /// </summary>
    [Parameter]
    public EventCallback<string> Selected { get; set; }

    /// <summary>
    /// Navigates to the related Pokemon.
    /// </summary>
    /// <returns>A task representing the callback.</returns>
    private Task SelectAsync()
        => Selected.InvokeAsync(Relation.SpeciesId);
}
