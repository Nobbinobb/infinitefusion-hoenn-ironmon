using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Renders one compact semantic icon for a nonstandard move-power value.
/// </summary>
public partial class MovePowerIndicatorIcon
{
    /// <summary>
    /// Gets or sets the semantic move-power indicator to draw.
    /// </summary>
    [Parameter]
    public MovePowerIndicator Indicator { get; set; }
}
