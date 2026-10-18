using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Shares paging presentation while allowing views to retain their range and availability rules.
/// </summary>
public partial class ObsidianPagingControls
{
    /// <summary>
    /// Gets or sets the accessible name of the navigation.
    /// </summary>
    [Parameter]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the localized range or page-number text.
    /// </summary>
    [Parameter]
    public string RangeText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the previous action is unavailable.
    /// </summary>
    [Parameter]
    public bool PreviousDisabled { get; set; }

    /// <summary>
    /// Gets or sets whether the next action is unavailable.
    /// </summary>
    [Parameter]
    public bool NextDisabled { get; set; }

    /// <summary>
    /// Gets or sets the previous-page callback.
    /// </summary>
    [Parameter]
    public EventCallback Previous { get; set; }

    /// <summary>
    /// Gets or sets the next-page callback.
    /// </summary>
    [Parameter]
    public EventCallback Next { get; set; }
}
