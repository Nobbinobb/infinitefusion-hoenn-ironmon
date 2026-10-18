using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Presents a setting label with optional mouse and keyboard accessible help.
/// </summary>
public partial class ObsidianSettingLabel
{
    private const string HelpLabelKey = "Settings.Help.Label";
    private const string HelpIdPrefix = "setting-help-";
    private const string IdFormat = "N";
    private readonly string _helpId = HelpIdPrefix + Guid.NewGuid().ToString(IdFormat);

    /// <summary>
    /// Gets or sets the visible setting label.
    /// </summary>
    [Parameter]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the control identified by the label, when present.
    /// </summary>
    [Parameter]
    public string? ControlId { get; set; }

    /// <summary>
    /// Gets or sets the explanatory text shown by the help button.
    /// </summary>
    [Parameter]
    public string? Description { get; set; }
}
