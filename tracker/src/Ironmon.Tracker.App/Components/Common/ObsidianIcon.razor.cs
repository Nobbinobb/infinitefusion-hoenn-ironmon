using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Renders reusable outline icons independently of the installed platform icon font.
/// </summary>
public partial class ObsidianIcon
{
    /// <summary>
    /// Gets or sets the semantic icon to display.
    /// </summary>
    [Parameter]
    public ObsidianIconKind Kind { get; set; }
}
