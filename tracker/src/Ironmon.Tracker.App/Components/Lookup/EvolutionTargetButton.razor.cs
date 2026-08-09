using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Renders one navigable generated evolution target.
/// </summary>
public partial class EvolutionTargetButton
{
    private string? _spriteKey;
    private string? _spriteSource;

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
    /// Refreshes the local sprite when the target changes.
    /// </summary>
    protected override void OnParametersSet()
    {
        string? key = GameRoot is null || Target.SpritePath is null ? null : $"{GameRoot}|{Target.SpritePath}";
        if (key == _spriteKey)
            return;

        _spriteKey = key;
        _spriteSource = LocalSpriteLoader.Load(GameRoot, Target.SpritePath);
    }

    /// <summary>
    /// Navigates to the generated destination.
    /// </summary>
    /// <returns>A task representing the callback.</returns>
    private Task SelectAsync()
        => Selected.InvokeAsync(Target.SpeciesId);
}
