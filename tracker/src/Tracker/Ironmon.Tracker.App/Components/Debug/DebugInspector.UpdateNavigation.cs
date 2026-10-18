using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Restores diagnostic navigation through the existing capability gates and normal request paths.
/// </summary>
public partial class DebugInspector
{
    private const string UpdateNavigationKey = "inspector";

    /// <summary>
    /// Gets the optional updater navigation registry supplied by the tracker layout.
    /// </summary>
    [CascadingParameter]
    private TrackerUpdateNavigation? UpdateNavigation { get; set; }

    /// <summary>
    /// Restores a selected diagnostic page only after live connection and access state are available.
    /// </summary>
    /// <returns>Whether the saved page handled this parameter update.</returns>
    private async Task<bool> RestoreUpdateNavigationAsync()
    {
        UpdateNavigation?.Register(UpdateNavigationKey, () => new UpdateSelection(_selectedPage, _selectedPokemonPage, _selectedTarget, _requestedLookupSpeciesId));
        if (ConnectionState.Snapshot.Status != TrackerConnectionStatus.Connected)
            return false;

        if (UpdateNavigation?.Take<UpdateSelection>(UpdateNavigationKey) is not { } saved || !Enum.IsDefined(saved.Page) || !Enum.IsDefined(saved.PokemonPage) || saved.Target is null || saved.Target.Length > 128 || saved.Species?.Length > 64 || !CanShowPage(saved.Page))
            return false;

        _selectedTarget = saved.Target;
        EnsureSelectedTargetAvailable();
        _targetInitialized = true;
        _observedPlayer = Player;
        _observedEnemies = Enemies;
        _requestedLookupSpeciesId = saved.Species;
        if (CanShowPokemonInformationPage(saved.PokemonPage))
            _selectedPokemonPage = saved.PokemonPage;

        await SelectPageAsync(saved.Page);
        return true;
    }

    /// <summary>
    /// Stores diagnostic route identities without storing diagnostic data or development actions.
    /// </summary>
    /// <remarks>
    /// Constructs a bounded selection that remains subject to normal access checks.
    /// </remarks>
    /// <param name="Page">The inspector page.</param>
    /// <param name="PokemonPage">The selected information section.</param>
    /// <param name="Target">The player or enemy selection.</param>
    /// <param name="Species">The requested generated lookup species.</param>
    private sealed record UpdateSelection(DebugInspectorPage Page, PokemonInformationPage PokemonPage, string Target, string? Species);
}
