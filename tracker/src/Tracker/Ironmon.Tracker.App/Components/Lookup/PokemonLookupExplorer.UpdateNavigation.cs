using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Restores the selected search or species through normal lookup authorization after update relaunch.
/// </summary>
public partial class PokemonLookupExplorer
{
    private const string UpdateNavigationKey = "pokemon-lookup";

    /// <summary>
    /// Gets the optional updater navigation registry supplied by the tracker layout.
    /// </summary>
    [CascadingParameter]
    private TrackerUpdateNavigation? UpdateNavigation { get; set; }

    /// <summary>
    /// Registers the mounted selection and reloads saved identities without trusting cached gameplay data.
    /// </summary>
    /// <returns>Whether the restored selection handled this parameter update.</returns>
    private async Task<bool> RestoreUpdateNavigationAsync()
    {
        UpdateNavigation?.Register(UpdateNavigationKey, () => new UpdateSelection(Recipe?.RunId, DebugMode, _query, _searchPagination.PageIndex, _searched, _showDetail ? _currentSpeciesId : null));
        if (DebugMode && !TrackerDiagnosticCapabilityRules.CanUseActivePokemonLookup(Connection))
            return false;

        if (UpdateNavigation?.Take<UpdateSelection>(UpdateNavigationKey) is not { } saved || saved.Run != Recipe?.RunId || saved.Debug != DebugMode || saved.Query is null || saved.Query.Length > 128 || saved.Page is < 0 or > 10000 || saved.Species?.Length > 64)
            return false;

        _query = saved.Query;
        if (saved.Searched && !string.IsNullOrWhiteSpace(_query))
            await SearchPageAsync(saved.Page);

        if (!string.IsNullOrWhiteSpace(saved.Species))
            await LoadPokemonAsync(saved.Species);

        _observedRequestedSpeciesId = RequestedSpeciesId;
        return true;
    }

    /// <summary>
    /// Stores only search text, paging and a selected species identity.
    /// </summary>
    /// <remarks>
    /// Constructs the bounded selection for its original lookup scope.
    /// </remarks>
    /// <param name="Run">The archived run, or null for authorized live lookup.</param>
    /// <param name="Debug">Whether the source is the active run.</param>
    /// <param name="Query">The entered search query.</param>
    /// <param name="Page">The search page.</param>
    /// <param name="Searched">Whether results were requested.</param>
    /// <param name="Species">The selected species identity.</param>
    private sealed record UpdateSelection(string? Run, bool Debug, string Query, int Page, bool Searched, string? Species);
}
