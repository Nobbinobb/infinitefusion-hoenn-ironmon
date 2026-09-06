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
    /// Gets or sets whether changes are temporarily unavailable.
    /// </summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>
    /// Gets or sets the surrounding layout classes.
    /// </summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>
    /// Gets or sets whether descriptive text precedes the switch.
    /// </summary>
    [Parameter]
    public bool LabelFirst { get; set; }

    /// <summary>
    /// Gets or sets secondary explanation displayed in the label-first layout.
    /// </summary>
    [Parameter]
    public string? Description { get; set; }

    /// <summary>
    /// Forwards the native checkbox state to the owning view.
    /// </summary>
    /// <param name="args">The checkbox change event.</param>
    /// <returns>A task representing delivery of the updated state.</returns>
    private Task HandleChangeAsync(ChangeEventArgs args)
        => ValueChanged.InvokeAsync(args.Value is true);
}
