using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Selects the normal and component-specific evolution candidate lists available for one source.
/// </summary>
public partial class EvolutionCandidateLists
{
    private EvolutionCandidateSide _selectedFusionSide = EvolutionCandidateSide.Head;
    private string? _observedSource;

    /// <summary>
    /// Gets or sets whether the containing research card uses the redesigned presentation.
    /// </summary>
    [CascadingParameter(Name = nameof(PokemonLookupCard.ResearchRedesigned))]
    public bool Redesigned { get; set; }

    /// <summary>
    /// Gets or sets the source species identifier.
    /// </summary>
    [Parameter]
    public required string SpeciesId { get; set; }

    /// <summary>
    /// Gets or sets whether a normal candidate list is available.
    /// </summary>
    [Parameter]
    public bool HasNormal { get; set; }

    /// <summary>
    /// Gets or sets whether a fusion Head candidate list is available.
    /// </summary>
    [Parameter]
    public bool HasHead { get; set; }

    /// <summary>
    /// Gets or sets whether a fusion Body candidate list is available.
    /// </summary>
    [Parameter]
    public bool HasBody { get; set; }

    /// <summary>
    /// Gets or sets the completed-run recipe when lookup is not using Debug.
    /// </summary>
    [Parameter]
    public CompletedRunRecipePayload? Recipe { get; set; }

    /// <summary>
    /// Gets or sets whether requests use the authorized active Debug run.
    /// </summary>
    [Parameter]
    public bool DebugMode { get; set; }

    /// <summary>
    /// Gets or sets the optional live source that securely supplies the represented species.
    /// </summary>
    [Parameter]
    public DebugPokemonTarget? DebugTarget { get; set; }

    /// <summary>
    /// Gets or sets the enemy battler position when the live source is an enemy.
    /// </summary>
    [Parameter]
    public int? DebugEnemyPosition { get; set; }

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets whether the compact graph action is available.
    /// </summary>
    [Parameter]
    public bool ShowGraphAction { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the evolution graph is opened.
    /// </summary>
    [Parameter]
    public EventCallback GraphOpened { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a candidate is selected.
    /// </summary>
    [Parameter]
    public EventCallback<string> Selected { get; set; }

    /// <summary>
    /// Selects the first available fusion side when the displayed source changes.
    /// </summary>
    protected override void OnParametersSet()
    {
        string source = $"{SpeciesId}|{HasHead}|{HasBody}";
        if (source == _observedSource)
            return;

        _observedSource = source;
        _selectedFusionSide = HasHead ? EvolutionCandidateSide.Head : EvolutionCandidateSide.Body;
    }

    /// <summary>
    /// Selects the visible fusion evolution candidate side.
    /// </summary>
    /// <param name="side">The Head or Body side to display.</param>
    private void SelectFusionSide(EvolutionCandidateSide side)
        => _selectedFusionSide = side;

    /// <summary>
    /// Gets the CSS class for one fusion-side tab.
    /// </summary>
    /// <param name="side">The represented fusion side.</param>
    /// <returns>The tab CSS classes.</returns>
    private string GetSideTabClass(EvolutionCandidateSide side)
        => _selectedFusionSide == side ? TrackerUiConstants.SelectedCssClass : string.Empty;
}
