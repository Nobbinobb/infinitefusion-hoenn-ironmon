using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Provides the common redesigned modal frame for migrated views.
/// </summary>
public partial class ObsidianDialog
{
    /// <summary>
    /// Gets or sets the dialog heading and accessible name.
    /// </summary>
    [Parameter]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the secondary context above the heading.
    /// </summary>
    [Parameter]
    public string? Eyebrow { get; set; }

    /// <summary>
    /// Gets or sets the dialog body.
    /// </summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Gets or sets the dismissal callback.
    /// </summary>
    [Parameter]
    public EventCallback Closed { get; set; }

    /// <summary>
    /// Gets or sets optional panel layout classes.
    /// </summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>
    /// Gets or sets a context-specific accessible close label.
    /// </summary>
    [Parameter]
    public string? CloseLabel { get; set; }

    /// <summary>
    /// Gets or sets custom heading content such as a move category icon and colored title.
    /// </summary>
    [Parameter]
    public RenderFragment? HeadingContent { get; set; }
}
