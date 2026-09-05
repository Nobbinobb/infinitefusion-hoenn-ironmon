using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Shares the identity, health, and detail layout between redesigned Pokémon cards.
/// </summary>
public partial class ObsidianIdentityPanel
{
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
    /// Gets or sets the accessible name for the enlarged sprite.
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
    /// Gets or sets the accessible description of the detail actions.
    /// </summary>
    [Parameter]
    public string DetailsLabel { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the visible level, species, types, or waiting explanation.
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
