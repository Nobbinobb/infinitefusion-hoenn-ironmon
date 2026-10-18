using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Settings;

/// <summary>
/// Displays a binding as a single compact chip with consistent controller symbols.
/// </summary>
public partial class ControlBindingChip
{
    private const string _combinationClass = "tracker-control-combination";

    /// <summary>
    /// Gets or sets the keyboard chord, or an empty chord for controller bindings.
    /// </summary>
    [Parameter]
    public int[] Keyboard { get; set; } = [];

    /// <summary>
    /// Gets or sets the normalized controller chord.
    /// </summary>
    [Parameter]
    public string[] Controller { get; set; } = [];

    /// <summary>
    /// Gets or sets whether to use PlayStation button symbols.
    /// </summary>
    [Parameter]
    public bool PlayStation { get; set; }
}
