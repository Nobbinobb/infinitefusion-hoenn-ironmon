using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Groups related controls using the shared redesigned section surface.
/// </summary>
public partial class ObsidianSection
{
    /// <summary>
    /// Gets or sets optional layout classes for the section's host view.
    /// </summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>
    /// Gets or sets the section heading.
    /// </summary>
    [Parameter]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the decorative section icon.
    /// </summary>
    [Parameter]
    public ObsidianIconKind Icon { get; set; }

    /// <summary>
    /// Gets or sets optional content beside the heading.
    /// </summary>
    [Parameter]
    public RenderFragment? HeadingContent { get; set; }

    /// <summary>
    /// Gets or sets the section controls and supporting content.
    /// </summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
