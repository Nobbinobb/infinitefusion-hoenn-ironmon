using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Renders one navigable generated evolution candidate.
/// </summary>
public partial class EvolutionCandidateButton
{
    /// <summary>
    /// Gets or sets whether navigation is disabled while a replacement page loads.
    /// </summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>
    /// Gets or sets whether this content uses the redesigned research presentation.
    /// </summary>
    [CascadingParameter(Name = nameof(PokemonLookupCard.ResearchRedesigned))]
    public bool Redesigned { get; set; }

    /// <summary>
    /// Gets or sets the valid destination candidate.
    /// </summary>
    [Parameter]
    public EvolutionCandidateSnapshot Candidate { get; set; } = null!;

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the candidate is selected.
    /// </summary>
    [Parameter]
    public EventCallback<string> Selected { get; set; }

    /// <summary>
    /// Navigates to the selected candidate.
    /// </summary>
    /// <returns>A task representing the callback.</returns>
    private Task SelectAsync()
        => Selected.InvokeAsync(Candidate.SpeciesId);
}
