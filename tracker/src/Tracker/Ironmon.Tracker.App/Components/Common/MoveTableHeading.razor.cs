using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Keeps the player and enemy move columns aligned through one shared heading.
/// </summary>
public partial class MoveTableHeading
{
    /// <summary>
    /// Gets or sets the localized move-list heading.
    /// </summary>
    [Parameter]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets optional learnset progress beside the heading.
    /// </summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
