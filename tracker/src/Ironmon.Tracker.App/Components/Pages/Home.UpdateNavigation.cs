using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Pages;

/// <summary>
/// Preserves explicit navigation selections across an updater relaunch without changing the view's presentation.
/// </summary>
public partial class Home
{
    private bool _updateNavigationResuming;
    private const string UpdateNavigationKey = "home";

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
        UpdateNavigation?.Register(UpdateNavigationKey, () => new UpdateSelection(_selectedView, _accessOpen, _seedsOpen, _settingsOpen, _selectedEnemyId));
        if (UpdateNavigation?.Take<UpdateSelection>(UpdateNavigationKey) is not { } saved)
            return;

        if (!Enum.IsDefined(saved.View) || saved.Enemy?.Length > 128)
            return;

        _selectedView = saved.View;
        _accessOpen = saved.Access;
        _seedsOpen = saved.Seeds && !_accessOpen;
        _settingsOpen = saved.Settings && !_accessOpen && !_seedsOpen;
        _selectedEnemyId = saved.Enemy;
        _updateNavigationResuming = true;
    }

    /// <summary>
    /// Stores navigation identities only; existing data and access checks remain authoritative after reconnection.
    /// </summary>
    /// <remarks>
    /// Constructs the bounded component resume selection.
    /// </remarks>
    /// <param name="View">The saved View navigation selection.</param>
    /// <param name="Access">The saved Access navigation selection.</param>
    /// <param name="Seeds">The saved Seeds navigation selection.</param>
    /// <param name="Settings">The saved Settings navigation selection.</param>
    /// <param name="Enemy">The saved Enemy navigation selection.</param>
    private sealed record UpdateSelection(TrackerView View, bool Access, bool Seeds, bool Settings, string? Enemy);
}
