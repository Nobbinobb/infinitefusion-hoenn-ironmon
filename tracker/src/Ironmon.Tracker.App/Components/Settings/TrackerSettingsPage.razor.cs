using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Settings;

/// <summary>
/// Presents and edits tracker-owned user settings.
/// </summary>
public partial class TrackerSettingsPage
{
    /// <summary>
    /// Gets or initializes whether starter selection is controlled automatically.
    /// </summary>
    [Parameter]
    public bool AutoSelectStarter { get; set; }

    /// <summary>
    /// Gets or initializes the callback raised when automatic starter selection changes.
    /// </summary>
    [Parameter]
    public EventCallback<bool> AutoSelectStarterChanged { get; set; }

    /// <summary>
    /// Gets or initializes the current setting synchronization status.
    /// </summary>
    [Parameter]
    public string? Status { get; set; }

    /// <summary>
    /// Applies a changed automatic starter-selection value.
    /// </summary>
    /// <param name="args">The checkbox change event.</param>
    /// <returns>A task representing callback dispatch.</returns>
    private Task HandleAutoSelectChanged(ChangeEventArgs args)
        => AutoSelectStarterChanged.InvokeAsync(args.Value is bool enabled && enabled);
}
