using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Groups expandable content with the shared redesigned disclosure heading.
/// </summary>
public partial class ObsidianDisclosure
{
    /// <summary>
    /// Gets or sets optional classes for the host view's layout.
    /// </summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>
    /// Gets or sets the plain heading when no custom heading is supplied.
    /// </summary>
    [Parameter]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional decorative heading icon.
    /// </summary>
    [Parameter]
    public ObsidianIconKind? Icon { get; set; }

    /// <summary>
    /// Gets or sets whether the disclosure starts expanded.
    /// </summary>
    [Parameter]
    public bool InitiallyOpen { get; set; }

    /// <summary>
    /// Gets or sets rich heading content in place of the plain title.
    /// </summary>
    [Parameter]
    public RenderFragment? HeadingContent { get; set; }

    /// <summary>
    /// Gets or sets heading actions whose handlers suppress disclosure toggling.
    /// </summary>
    [Parameter]
    public RenderFragment? Actions { get; set; }

    /// <summary>
    /// Gets or sets the content revealed by expanding the heading.
    /// </summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
