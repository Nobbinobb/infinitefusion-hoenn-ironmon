using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Presents one compact metric with a shared outline icon and its label.
/// </summary>
public partial class ObsidianMetric
{
    /// <summary>
    /// Gets or sets the icon identifying this metric.
    /// </summary>
    [Parameter]
    public ObsidianIconKind Icon { get; set; }

    /// <summary>
    /// Gets or sets the formatted value.
    /// </summary>
    [Parameter]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the localized label.
    /// </summary>
    [Parameter]
    public string Label { get; set; } = string.Empty;
}
