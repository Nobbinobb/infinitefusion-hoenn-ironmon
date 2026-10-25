using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Pairs a completion indicator with its visible localized label.
/// </summary>
public partial class ObsidianStatus
{
    /// <summary>
    /// Gets or sets whether the represented action is complete.
    /// </summary>
    [Parameter]
    public bool Complete { get; set; }

    /// <summary>
    /// Gets or sets the localized status label.
    /// </summary>
    [Parameter]
    public string Label { get; set; } = string.Empty;
}
