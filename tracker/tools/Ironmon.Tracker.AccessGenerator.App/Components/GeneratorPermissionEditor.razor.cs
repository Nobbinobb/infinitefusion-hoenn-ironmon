namespace Ironmon.Tracker.AccessGenerator.App.Components;

/// <summary>
/// Displays grouped direct selections and locked implied grants in a stable scrolling region.
/// </summary>
public partial class GeneratorPermissionEditor
{
    private static readonly IReadOnlyList<GeneratorCapabilityGroup> _groups = GeneratorCapabilityGroupCatalog.All;
    private GeneratorCapabilityGroup _activeGroup = _groups[0];

    /// <summary>
    /// Gets or sets the generator selection shared with the review.
    /// </summary>
    [Parameter, EditorRequired]
    public DiagnosticAccessSelection Selection { get; set; } = null!;

    /// <summary>
    /// Gets or sets the callback for one direct grant change.
    /// </summary>
    [Parameter]
    public EventCallback<(string Capability, bool Selected)> Changed { get; set; }

    /// <summary>
    /// Selects a group and remounts its scroll region at the beginning.
    /// </summary>
    /// <param name="group">The group to display.</param>
    private void SelectGroup(GeneratorCapabilityGroup group) => _activeGroup = group;
}
