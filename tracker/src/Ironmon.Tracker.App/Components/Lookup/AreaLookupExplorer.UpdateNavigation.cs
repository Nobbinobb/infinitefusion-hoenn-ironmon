using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Restores area navigation through ordinary authorized requests after an updater relaunch.
/// </summary>
public partial class AreaLookupExplorer
{
    private const string UpdateNavigationKey = "areas";
    private static readonly string[] _updateEnvironments = [_grassEnvironment, _caveEnvironment, _waterEnvironment, _fishingEnvironment, _crossEnvironment];

    /// <summary>
    /// Gets the optional updater navigation registry supplied by the tracker layout.
    /// </summary>
    [CascadingParameter]
    private TrackerUpdateNavigation? UpdateNavigation { get; set; }

    /// <summary>
    /// Captures bounded expansion and page identities without storing any area response payload.
    /// </summary>
    /// <returns>The mounted area navigation.</returns>
    private UpdateSelection CaptureUpdateNavigation()
    {
        UpdateArea[] areas = [.. _expandedAreas.Take(8).Select(area => new UpdateArea(area, GetDetailPagination(area).PageIndex, [.. _updateEnvironments.Where(environment => IsEnvironmentExpanded(area, environment)).Select(environment => new UpdateEnvironment(environment, GetDetailPagination(area, environment).PageIndex))]))];
        return new UpdateSelection(RunId, _selectedCategory, _areaPagination.PageIndex, areas, _selectedPokemonId, _returnTrainerId);
    }

    /// <summary>
    /// Restores only areas still present in the same run and re-requests their normally accessible details.
    /// </summary>
    /// <returns>The bounded ordinary lookup requests.</returns>
    private async Task RestoreUpdateNavigationAsync()
    {
        if (UpdateNavigation?.Take<UpdateSelection>(UpdateNavigationKey) is not { } saved || saved.Run != RunId || !Enum.IsDefined(saved.Category) || saved.Areas is null || saved.Areas.Length > 8 || saved.Page is < 0 or > 10000)
            return;

        if (FixedCategory is { } fixedCategory && fixedCategory != saved.Category)
            return;

        _selectedCategory = saved.Category;
        _areaPagination.Select(saved.Page);
        _areaPagination.Clamp(GetVisibleAreas().Count);
        foreach (var area in saved.Areas.Where(area => area is not null && _areas.Any(item => item.AreaId == area.Id)))
        {
            if (area.Page is < 0 or > 10000 || area.Environments is null || area.Environments.Length > 5)
                continue;

            _expandedAreas.Add(area.Id);
            await LoadAreaDetailsAsync(area.Id, Recipe is null, requestedPage: area.Page);
            foreach (var environment in area.Environments.Where(environment => environment is not null && _updateEnvironments.Contains(environment.Name) && environment.Page is >= 0 and <= 10000))
            {
                _expandedEnvironments.Add(GetEnvironmentKey(area.Id, environment.Name));
                await LoadAreaDetailsAsync(area.Id, Recipe is null, environment.Name, environment.Page);
            }
        }

        if (CanLookupPokemon && saved.Pokemon is { Length: > 0 and <= 64 } && saved.Trainer is { Length: > 0 and <= 128 })
        {
            _selectedPokemonId = saved.Pokemon;
            _returnTrainerId = saved.Trainer;
        }
    }

    /// <summary>
    /// Stores area identities and selections, excluding loaded game data.
    /// </summary>
    /// <remarks>
    /// Constructs a bounded navigation observation for the current run.
    /// </remarks>
    /// <param name="Run">The scope owning these selections.</param>
    /// <param name="Category">The selected area category.</param>
    /// <param name="Page">The summary page.</param>
    /// <param name="Areas">At most eight expanded areas.</param>
    /// <param name="Pokemon">The selected trainer species.</param>
    /// <param name="Trainer">The trainer return identity.</param>
    private sealed record UpdateSelection(string Run, AreaContentCategory Category, int Page, UpdateArea[] Areas, string? Pokemon, string? Trainer);

    /// <summary>
    /// Stores one expanded area and its nested page selections.
    /// </summary>
    /// <remarks>
    /// Constructs a navigation identity that must match an accessible area on restoration.
    /// </remarks>
    /// <param name="Id">The logical area identity.</param>
    /// <param name="Page">The area detail page.</param>
    /// <param name="Environments">The expanded encounter environments.</param>
    private sealed record UpdateArea(string Id, int Page, UpdateEnvironment[] Environments);

    /// <summary>
    /// Stores an expanded encounter environment without storing encounter data.
    /// </summary>
    /// <remarks>
    /// Constructs a page identity under the selected area.
    /// </remarks>
    /// <param name="Name">The known encounter environment.</param>
    /// <param name="Page">The zero-based detail page.</param>
    private sealed record UpdateEnvironment(string Name, int Page);
}
