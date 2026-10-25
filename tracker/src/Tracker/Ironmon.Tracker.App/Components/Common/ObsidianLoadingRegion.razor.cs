using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Keeps existing content in place and temporarily prevents interaction while its replacement loads.
/// </summary>
public partial class ObsidianLoadingRegion
{
    /// <summary>
    /// Gets or sets whether a request is in progress.
    /// </summary>
    [Parameter]
    public bool Loading { get; set; }

    /// <summary>
    /// Gets or sets whether existing content supplies the region's height.
    /// </summary>
    [Parameter]
    public bool HasContent { get; set; }

    /// <summary>
    /// Gets or sets the localized loading announcement.
    /// </summary>
    [Parameter]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the retained content, including any request error.
    /// </summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
