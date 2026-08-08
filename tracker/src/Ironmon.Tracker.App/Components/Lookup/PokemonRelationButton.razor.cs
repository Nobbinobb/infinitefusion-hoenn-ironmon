using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Renders a navigable Pokemon relationship with its local sprite.
/// </summary>
public partial class PokemonRelationButton
{
    private string? _spriteKey;
    private string? _spriteSource;

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
    /// Refreshes the local sprite when the relation changes.
    /// </summary>
    protected override void OnParametersSet()
    {
        string? key = GameRoot is null || Relation.SpritePath is null ? null : $"{GameRoot}|{Relation.SpritePath}";
        if (key == _spriteKey)
            return;

        _spriteKey = key;
        _spriteSource = LocalSpriteLoader.Load(GameRoot, Relation.SpritePath);
    }

    /// <summary>
    /// Navigates to the related Pokemon.
    /// </summary>
    /// <returns>A task representing the callback.</returns>
    private Task SelectAsync()
        => Selected.InvokeAsync(Relation.SpeciesId);
}
