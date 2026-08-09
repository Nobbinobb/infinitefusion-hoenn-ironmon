using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Renders one navigable generated evolution candidate.
/// </summary>
public partial class EvolutionCandidateButton
{
    private string? _spriteKey;
    private string? _spriteSource;

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
    /// Refreshes the local sprite when the candidate changes.
    /// </summary>
    protected override void OnParametersSet()
    {
        string? key = GameRoot is null || Candidate.SpritePath is null ? null : $"{GameRoot}|{Candidate.SpritePath}";
        if (key == _spriteKey)
            return;

        _spriteKey = key;
        _spriteSource = LocalSpriteLoader.Load(GameRoot, Candidate.SpritePath);
    }

    /// <summary>
    /// Navigates to the selected candidate.
    /// </summary>
    /// <returns>A task representing the callback.</returns>
    private Task SelectAsync()
        => Selected.InvokeAsync(Candidate.SpeciesId);
}
