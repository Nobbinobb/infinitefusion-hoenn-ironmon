using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Shares the identity, health, and detail layout between redesigned Pokémon cards.
/// </summary>
public partial class ObsidianIdentityPanel
{
    private PokemonSpriteComponents? _components;

    /// <summary>
    /// Gets the application-wide viewer used by the sprite and component-name line.
    /// </summary>
    [Inject]
    private PokemonSpriteDialogService Dialog { get; set; } = null!;

    /// <summary>
    /// Loads visual-component metadata for the currently displayed sprite.
    /// </summary>
    protected override void OnParametersSet()
        => _components = LocalSpriteComponentsLoader.Load(GameRoot, SpritePath);

    /// <summary>
    /// Opens the shared sprite viewer with the visual head selected when component images are available.
    /// </summary>
    private void OpenComponents()
    {
        string? source = LocalSpriteLoader.Load(GameRoot, SpritePath);
        if (source is not null)
            Dialog.Open(source, SpriteLabel, 288, GameRoot, SpritePath, PokemonSpritePart.Head);
    }

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets the game-relative sprite path.
    /// </summary>
    [Parameter]
    public string? SpritePath { get; set; }

    /// <summary>
    /// Gets or sets the complete sprite's accessible name and shared viewer heading.
    /// </summary>
    [Parameter]
    public string SpriteLabel { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the visible identity heading.
    /// </summary>
    [Parameter]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the heading identifying the Pokémon's role or transformation.
    /// </summary>
    [Parameter]
    public string Kicker { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional level shown beside the role heading when visual components are available.
    /// </summary>
    [Parameter]
    public int? Level { get; set; }

    /// <summary>
    /// Gets or sets the card-supplied gender shown beside the level when visual components are available.
    /// </summary>
    [Parameter]
    public string? Gender { get; set; }

    /// <summary>
    /// Gets or sets the type controls rendered below the component names or fallback metadata.
    /// </summary>
    [Parameter]
    public RenderFragment? Types { get; set; }

    /// <summary>
    /// Gets or sets the accessible description of the detail actions.
    /// </summary>
    [Parameter]
    public string DetailsLabel { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the species, level, or waiting explanation shown when visual-component metadata is unavailable.
    /// </summary>
    [Parameter]
    public RenderFragment? Metadata { get; set; }

    /// <summary>
    /// Gets or sets the visibility-filtered identity details and actions.
    /// </summary>
    [Parameter]
    public RenderFragment? Details { get; set; }

    /// <summary>
    /// Gets or sets the visible or explicitly unknown health information.
    /// </summary>
    [Parameter]
    public RenderFragment? Health { get; set; }

    /// <summary>
    /// Gets or sets the card-specific summary and supplementary actions.
    /// </summary>
    [Parameter]
    public RenderFragment? Footer { get; set; }
}
