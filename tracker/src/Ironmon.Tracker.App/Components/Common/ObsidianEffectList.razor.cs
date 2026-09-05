using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Renders compact effect labels with affected moves or recovery outcomes hidden until the label is clicked.
/// </summary>
public partial class ObsidianEffectList
{
    private string? _expandedLabel;

    /// <summary>
    /// Gets or sets whether triggers use vertically balanced recovery rows.
    /// </summary>
    [Parameter]
    public bool Recovery { get; set; }

    /// <summary>
    /// Gets or sets the section heading.
    /// </summary>
    [Parameter]
    public string Heading { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the already privacy-filtered compact effects.
    /// </summary>
    [Parameter]
    public IReadOnlyList<DefenseEffectSnapshot> Effects { get; set; } = [];

    /// <summary>
    /// Toggles one effect's supporting information below the label group.
    /// </summary>
    /// <param name="label">The visible effect label identifying the disclosure.</param>
    private void Toggle(string label) 
        => _expandedLabel = _expandedLabel == label ? null : label;

    /// <summary>
    /// Gets one entry per available label, preserving independent recovery contributions.
    /// </summary>
    private IReadOnlyList<DefenseEffectSnapshot> VisibleEffects() 
        => DefenseEffectPresentation.Merge(Effects);
}
