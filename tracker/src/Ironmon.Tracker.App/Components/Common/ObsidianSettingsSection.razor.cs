using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Groups related preferences using the shared redesigned settings surface.
/// </summary>
public partial class ObsidianSettingsSection
{
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
    /// Gets or sets the preference controls.
    /// </summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
