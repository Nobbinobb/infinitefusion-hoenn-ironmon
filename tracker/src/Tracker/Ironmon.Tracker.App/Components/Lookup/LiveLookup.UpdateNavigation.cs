using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Preserves explicit navigation selections across an updater relaunch without changing the view's presentation.
/// </summary>
public partial class LiveLookup
{
    private const string UpdateNavigationKey = "live-lookup";
    private const string UpdateCoverageKey = "coverage-selection";

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
        UpdateNavigation?.Register(UpdateNavigationKey, () => new UpdateSelection(_selectedSection));
        if (UpdateNavigation?.Take<UpdateSelection>(UpdateNavigationKey) is not { } saved)
            return;

        if (Enum.IsDefined(saved.Section))
            _selectedSection = saved.Section;
    }

    /// <summary>
    /// Restores hypothetical type choices only after the same current Pokémon has reconnected.
    /// </summary>
    private void RestoreUpdateCoverage()
    {
        UpdateNavigation?.Register(UpdateCoverageKey, () => new CoverageSelection(_coverageSelection.PokemonId, _coverageSelection.IsManual, [.. _coverageSelection.SelectedTypes]));
        if (_coverageSelection.PokemonId is null)
            return;

        if (UpdateNavigation?.Take<CoverageSelection>(UpdateCoverageKey) is not { } saved || !saved.Manual || saved.Pokemon != _coverageSelection.PokemonId || saved.Types is null || saved.Types.Length > 18 || saved.Types.Any(type => !PokemonTypeCatalog.StandardTypes.Contains(type)))
            return;

        foreach (var type in PokemonTypeCatalog.StandardTypes.Where(type => saved.Types.Contains(type) != _coverageSelection.SelectedTypes.Contains(type)))
            _coverageSelection.Toggle(type);
    }

    /// <summary>
    /// Stores selected attacking types without persisting player moves or generated coverage data.
    /// </summary>
    /// <remarks>
    /// Constructs a bounded hypothetical coverage selection.
    /// </remarks>
    /// <param name="Pokemon">The original player identity.</param>
    /// <param name="Manual">Whether the player edited the selection.</param>
    /// <param name="Types">The selected standard type identifiers.</param>
    private sealed record CoverageSelection(string? Pokemon, bool Manual, string[] Types);

    /// <summary>
    /// Stores navigation identities only; existing data and access checks remain authoritative after reconnection.
    /// </summary>
    /// <remarks>
    /// Constructs the bounded component resume selection.
    /// </remarks>
    /// <param name="Section">The saved Section navigation selection.</param>
    private sealed record UpdateSelection(LiveLookupSection Section);
}
