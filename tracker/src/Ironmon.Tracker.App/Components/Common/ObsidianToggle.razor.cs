using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Presents a labeled switch using the shared settings appearance.
/// </summary>
public partial class ObsidianToggle
{
    /// <summary>
    /// Gets or sets the visible and accessible switch label.
    /// </summary>
    [Parameter]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the switch is enabled.
    /// </summary>
    [Parameter]
    public bool Value { get; set; }

    /// <summary>
    /// Gets or sets the callback receiving the requested switch state.
    /// </summary>
    [Parameter]
    public EventCallback<bool> ValueChanged { get; set; }

    /// <summary>
    /// Forwards the native checkbox state to the owning view.
    /// </summary>
    /// <param name="args">The checkbox change event.</param>
    /// <returns>A task representing delivery of the updated state.</returns>
    private Task HandleChangeAsync(ChangeEventArgs args)
        => ValueChanged.InvokeAsync(args.Value is true);
}
