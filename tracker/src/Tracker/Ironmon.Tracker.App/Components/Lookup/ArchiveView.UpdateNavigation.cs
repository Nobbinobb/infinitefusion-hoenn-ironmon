using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Preserves explicit navigation selections across an updater relaunch without changing the view's presentation.
/// </summary>
public partial class ArchiveView
{
    private const string UpdateNavigationKey = "archive";

    /// <summary>
    /// Gets the optional update navigation registry supplied by the normal tracker layout.
    /// </summary>
    [CascadingParameter]
    private TrackerUpdateNavigation? UpdateNavigation { get; set; }

    /// <summary>
    /// Registers the mounted selection and restores only validated navigation from an updater relaunch.
    /// </summary>
    private void InitializeUpdateNavigation()
    {
        UpdateNavigation?.Register(UpdateNavigationKey, () => new UpdateSelection(_selectedSection, _selection.SelectedRunId, _selection.IsExpanded, _showHistory));
        if (UpdateNavigation?.Take<UpdateSelection>(UpdateNavigationKey) is not { } saved)
            return;

        if (!Enum.IsDefined(saved.Section) || saved.Run?.Length > 128)
            return;

        _selectedSection = saved.Section;
        if (saved.Run is not null && _recipes.Any(recipe => recipe.RunId == saved.Run))
            _selection.Select(saved.Run);

        _selection.SetExpanded(saved.Expanded);
        _showHistory = saved.History;
    }

    /// <summary>
    /// Stores navigation identities only; existing data and access checks remain authoritative after reconnection.
    /// </summary>
    /// <remarks>
    /// Constructs the bounded component resume selection.
    /// </remarks>
    /// <param name="Section">The saved Section navigation selection.</param>
    /// <param name="Run">The saved Run navigation selection.</param>
    /// <param name="Expanded">The saved Expanded navigation selection.</param>
    /// <param name="History">The saved History navigation selection.</param>
    private sealed record UpdateSelection(ArchiveSection Section, string? Run, bool Expanded, bool History);
}
