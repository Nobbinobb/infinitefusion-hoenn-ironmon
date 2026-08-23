using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents lazily loaded area progress for an active or archived run.
/// </summary>
public partial class AreaLookupExplorer : IDisposable
{
    private const int _pageSize = 10;
    private const string _summaryRequestKey = "summary";
    private static readonly AreaContentCategory[] Categories = [AreaContentCategory.Trainer, AreaContentCategory.Encounter, AreaContentCategory.Item];
    private readonly Dictionary<string, AreaLookupDetailResponsePayload> _details = [];
    private readonly Dictionary<string, PaginationState> _detailPagination = [];
    private readonly Dictionary<string, string> _detailErrors = [];
    private readonly Dictionary<string, string?> _encounterSpriteSources = [];
    private readonly Dictionary<string, string?> _trainerSpriteSources = [];
    private readonly HashSet<string> _expandedAreas = [];
    private readonly HashSet<string> _loadingDetails = [];
    private readonly LatestRequestCoordinator<string> _detailRequests = new();
    private readonly LatestRequestCoordinator<string> _summaryRequests = new();
    private readonly PaginationState _areaPagination = new(_pageSize);
    private IReadOnlyList<AreaSummaryPayload> _areas = [];
    private string? _enlargedSpriteLabel;
    private string? _enlargedSpriteSource;
    private AreaContentCategory _selectedCategory = AreaContentCategory.Trainer;
    private string? _observedSourceKey;
    private string? _error;
    private bool _loadingSummaries;

    /// <summary>
    /// Gets or initializes the active game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Connection { get; set; } = null!;

    /// <summary>
    /// Gets or initializes tracker-owned area discovery persistence.
    /// </summary>
    [Inject]
    private AreaDiscoveryStore Discoveries { get; set; } = null!;

    /// <summary>
    /// Gets or initializes tracker-owned diagnostic access.
    /// </summary>
    [Inject]
    private DiagnosticAccessService AccessService { get; set; } = null!;

    /// <summary>
    /// Gets or sets the archived recipe, or null for the active run.
    /// </summary>
    [Parameter]
    public CompletedRunRecipePayload? Recipe { get; set; }

    /// <summary>
    /// Gets or sets the stable key identifying the selected lookup source.
    /// </summary>
    [Parameter]
    public required string SourceKey { get; set; }

    /// <summary>
    /// Gets or sets the selected run identifier.
    /// </summary>
    [Parameter]
    public required string RunId { get; set; }

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets a parent-controlled category, or null to expose the component's own category tabs.
    /// </summary>
    [Parameter]
    public AreaContentCategory? FixedCategory { get; set; }

    /// <summary>
    /// Gets or sets whether the shared world-lookup heading is shown.
    /// </summary>
    [Parameter]
    public bool ShowHeading { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the component's own category tabs are shown.
    /// </summary>
    [Parameter]
    public bool ShowCategoryTabs { get; set; } = true;

    /// <summary>
    /// Subscribes to tracker-owned discoveries received regardless of the visible page.
    /// </summary>
    protected override void OnInitialized()
    {
        Discoveries.Changed += HandleDiscoveryChanged;
        AccessService.Changed += HandleDiagnosticAccessChanged;
    }

    /// <summary>
    /// Reloads compact summaries when the selected run changes.
    /// </summary>
    /// <returns>A task representing summary loading.</returns>
    protected override async Task OnParametersSetAsync()
    {
        AreaContentCategory desiredCategory = FixedCategory ?? _selectedCategory;
        bool sourceChanged = _observedSourceKey != SourceKey;
        bool categoryChanged = desiredCategory != _selectedCategory;
        if (!sourceChanged && !categoryChanged)
            return;

        if (sourceChanged)
        {
            ResetPendingRequests();
            _observedSourceKey = SourceKey;
            _areas = [];
            _details.Clear();
            _detailPagination.Clear();
            _detailErrors.Clear();
            _encounterSpriteSources.Clear();
            _trainerSpriteSources.Clear();
        }
        else if (categoryChanged)
        {
            ResetPendingRequests();
        }

        _selectedCategory = desiredCategory;
        _areaPagination.Reset();
        _detailPagination.Clear();
        _expandedAreas.Clear();
        _loadingDetails.Clear();
        await LoadSummariesAsync(true);
    }

    /// <summary>
    /// Selects an area content category and closes groups from the previous category.
    /// </summary>
    /// <param name="category">The selected category.</param>
    private async Task SelectCategory(AreaContentCategory category)
    {
        ResetPendingRequests();
        _selectedCategory = category;
        _areaPagination.Reset();
        _detailPagination.Clear();
        _expandedAreas.Clear();
        await LoadSummariesAsync(true);
    }

    /// <summary>
    /// Expands or collapses an area, loading its selected category on first expansion.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <returns>A task representing optional detail loading.</returns>
    private async Task ToggleAreaAsync(string areaId)
    {
        if (!_expandedAreas.Add(areaId))
        {
            _expandedAreas.Remove(areaId);
            return;
        }

        string key = GetDetailKey(areaId);
        if (_details.ContainsKey(key) || _loadingDetails.Contains(key))
            return;

        await LoadAreaDetailsAsync(areaId, Recipe is null);
    }

    /// <summary>
    /// Loads one selected area category into the component cache.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <param name="forceRefresh">Whether to bypass the connection cache.</param>
    /// <returns>A task representing detail loading.</returns>
    private async Task LoadAreaDetailsAsync(string areaId, bool forceRefresh)
    {
        string key = GetDetailKey(areaId);
        AreaContentCategory category = _selectedCategory;
        CompletedRunRecipePayload? recipe = Recipe;
        LatestRequestLease<string> request = _detailRequests.Begin(key);
        _loadingDetails.Add(key);
        _detailErrors.Remove(key);
        try
        {
            AreaLookupDetailResponsePayload response = await Connection.GetAreaDetailsAsync(areaId, category, recipe, forceRefresh, request.CancellationToken);
            if (!request.IsCurrent)
                return;

            _details[key] = response;
            GetDetailPagination(areaId).Clamp(GetDetailEntryCount(response));
            foreach (AreaEncounterEntryPayload encounter in response.Encounters.Where(encounter => encounter.DetailsRevealed))
                _encounterSpriteSources[encounter.EntryId] = LocalSpriteLoader.Load(GameRoot, encounter.SpritePath);

            foreach (AreaTrainerEntryPayload trainer in response.Trainers.Where(trainer => trainer.DetailsRevealed))
            {
                foreach (AreaTrainerPokemonPayload pokemon in trainer.Party)
                    _trainerSpriteSources[GetTrainerPokemonKey(trainer, pokemon)] = LocalSpriteLoader.Load(GameRoot, pokemon.SpritePath);
            }
        }
        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedRequestException(exception))
        {
            if (request.IsCurrent)
                _detailErrors[key] = exception.Message;
        }
        finally
        {
            if (request.Complete())
                _loadingDetails.Remove(key);
        }
    }

    /// <summary>
    /// Loads the selected category's area list and tracker-owned completion counts.
    /// </summary>
    /// <param name="forceRefresh">Whether to request the game area list again.</param>
    /// <returns>A task representing the area-list request.</returns>
    private async Task LoadSummariesAsync(bool forceRefresh)
    {
        LatestRequestLease<string> request = _summaryRequests.Begin(_summaryRequestKey);
        AreaContentCategory category = _selectedCategory;
        CompletedRunRecipePayload? recipe = Recipe;

        _loadingSummaries = true;
        _error = null;
        try
        {
            AreaLookupSummaryResponsePayload response = await Connection.GetAreaSummariesAsync(category, recipe, forceRefresh, request.CancellationToken);
            if (!request.IsCurrent)
                return;

            _areas = response.Areas;
            _areaPagination.Clamp(GetVisibleAreas().Count);
        }
        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedRequestException(exception))
        {
            if (request.IsCurrent)
                _error = exception.Message;
        }
        finally
        {
            if (request.Complete())
                _loadingSummaries = false;
        }
    }

    /// <summary>
    /// Cancels work owned by the previous run or category.
    /// </summary>
    private void ResetPendingRequests()
    {
        _summaryRequests.CancelAll();
        _loadingSummaries = false;
        CancelDetailRequests();
    }

    /// <summary>
    /// Cancels every in-flight area-detail request and clears its loading state.
    /// </summary>
    private void CancelDetailRequests()
    {
        _detailRequests.CancelAll();
        _loadingDetails.Clear();
    }

    /// <summary>
    /// Refreshes counts and an expanded matching area after background discovery persistence.
    /// </summary>
    /// <param name="sender">The discovery store.</param>
    /// <param name="args">The persisted discovery change.</param>
    private void HandleDiscoveryChanged(object? sender, AreaDiscoveryChangedEventArgs args)
    {
        if (args.RunId != RunId)
            return;

        _ = InvokeAsync(async () =>
        {
            await LoadSummariesAsync(false);
            if (args.Category == _selectedCategory && _expandedAreas.Contains(args.AreaId)
                && !_loadingDetails.Contains(GetDetailKey(args.AreaId)))
                await LoadAreaDetailsAsync(args.AreaId, true);

            StateHasChanged();
        });
    }

    /// <summary>
    /// Removes cached active-run details immediately after diagnostic access changes.
    /// </summary>
    /// <param name="sender">The diagnostic-access service.</param>
    /// <param name="args">The empty change arguments.</param>
    private void HandleDiagnosticAccessChanged(object? sender, EventArgs args)
    {
        if (Recipe is not null)
            return;

        CancelDetailRequests();
        _details.Clear();
        _detailPagination.Clear();
        _detailErrors.Clear();
        _encounterSpriteSources.Clear();
        _trainerSpriteSources.Clear();
        _expandedAreas.Clear();
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Gets areas containing at least one entry in the selected category.
    /// </summary>
    /// <returns>The visible area summaries.</returns>
    private IReadOnlyList<AreaSummaryPayload> GetVisibleAreas()
        => [.. _areas.Where(area => GetTotal(area) > 0)];

    /// <summary>
    /// Gets the current ten-area page.
    /// </summary>
    /// <param name="areas">All visible areas.</param>
    /// <returns>The areas on the selected page.</returns>
    private IReadOnlyList<AreaSummaryPayload> GetPagedAreas(IReadOnlyList<AreaSummaryPayload> areas)
        => _areaPagination.GetPage(areas);

    /// <summary>
    /// Gets the current ten-entry page for one expanded area.
    /// </summary>
    /// <typeparam name="T">The area-entry type.</typeparam>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <param name="entries">All entries in the selected category.</param>
    /// <returns>The entries on the selected page.</returns>
    private IReadOnlyList<T> GetPagedEntries<T>(string areaId, IReadOnlyList<T> entries)
        => GetDetailPagination(areaId).GetPage(entries);

    /// <summary>
    /// Selects the previous area-summary page.
    /// </summary>
    private void PreviousAreaPage()
        => _areaPagination.Previous();

    /// <summary>
    /// Selects the next area-summary page.
    /// </summary>
    /// <param name="areaCount">The total visible area count.</param>
    private void NextAreaPage(int areaCount)
        => _areaPagination.Next(areaCount);

    /// <summary>
    /// Selects the previous entry page for one expanded area.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    private void PreviousDetailPage(string areaId)
        => GetDetailPagination(areaId).Previous();

    /// <summary>
    /// Selects the next entry page for one expanded area.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <param name="entryCount">The total entry count.</param>
    private void NextDetailPage(string areaId, int entryCount)
        => GetDetailPagination(areaId).Next(entryCount);

    /// <summary>
    /// Gets or creates pagination for one area and selected category.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <returns>The area's detail pagination.</returns>
    private PaginationState GetDetailPagination(string areaId)
    {
        string key = GetDetailKey(areaId);
        if (_detailPagination.TryGetValue(key, out PaginationState? pagination))
            return pagination;

        pagination = new PaginationState(_pageSize);
        _detailPagination.Add(key, pagination);
        return pagination;
    }

    /// <summary>
    /// Gets the total number of entries represented by one detail response.
    /// </summary>
    /// <param name="detail">The selected area detail.</param>
    /// <returns>The selected category's entry count.</returns>
    private static int GetDetailEntryCount(AreaLookupDetailResponsePayload detail) => detail.Category switch
    {
        AreaContentCategory.Trainer => detail.Trainers.Count,
        AreaContentCategory.Encounter => detail.Encounters.Count,
        AreaContentCategory.Item => detail.Items.Count,
        _ => 0
    };

    /// <summary>
    /// Formats an inclusive result range for one page.
    /// </summary>
    /// <param name="pagination">The selected pagination.</param>
    /// <param name="count">The total result count.</param>
    /// <returns>The visible range and total count.</returns>
    private string GetPageRangeText(PaginationState pagination, int count)
    {
        (int first, int last) = pagination.GetRange(count);
        return Text["Lookup.Search.ResultRange", first, last, count];
    }

    /// <summary>
    /// Gets the localized selected-category progress for one area.
    /// </summary>
    /// <param name="area">The area summary.</param>
    /// <returns>The completed and total count.</returns>
    private string GetProgressText(AreaSummaryPayload area)
    {
        return _selectedCategory switch
        {
            AreaContentCategory.Trainer => Text["Lookup.Areas.DefeatedProgress", area.TrainerDefeated, area.TrainerTotal],
            AreaContentCategory.Encounter => Text["Lookup.Areas.EncounteredProgress", area.Encountered, area.EncounterTotal],
            AreaContentCategory.Item => Text["Lookup.Areas.CollectedProgress", area.ItemsCollected, area.ItemTotal],
            _ => string.Empty
        };
    }

    /// <summary>
    /// Gets the entry total for the selected category.
    /// </summary>
    /// <param name="area">The area summary.</param>
    /// <returns>The entry total.</returns>
    private int GetTotal(AreaSummaryPayload area)
    {
        return _selectedCategory switch
        {
            AreaContentCategory.Trainer => area.TrainerTotal,
            AreaContentCategory.Encounter => area.EncounterTotal,
            AreaContentCategory.Item => area.ItemTotal,
            _ => 0
        };
    }

    /// <summary>
    /// Gets the localized category name.
    /// </summary>
    /// <param name="category">The category.</param>
    /// <returns>The category name.</returns>
    private string GetCategoryName(AreaContentCategory category)
    {
        return category switch
        {
            AreaContentCategory.Trainer => Text["Lookup.Areas.Trainers"],
            AreaContentCategory.Encounter => Text["Lookup.Areas.Encounters"],
            AreaContentCategory.Item => Text["Lookup.Areas.Items"],
            _ => string.Empty
        };
    }

    /// <summary>
    /// Gets the visual classes for one category tab.
    /// </summary>
    /// <param name="category">The category.</param>
    /// <returns>The tab classes.</returns>
    private string GetCategoryTabClass(AreaContentCategory category)
        => category == _selectedCategory ? "area-category-tab selected" : "area-category-tab";

    /// <summary>
    /// Gets whether an area is expanded in the selected category.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <returns>Whether the area is expanded.</returns>
    private bool IsExpanded(string areaId)
        => _expandedAreas.Contains(areaId);

    /// <summary>
    /// Builds the in-memory detail key for the selected category and area.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <returns>The detail key.</returns>
    private string GetDetailKey(string areaId)
        => $"{_selectedCategory}:{areaId}";

    /// <summary>
    /// Formats an encounter's level or inclusive level range.
    /// </summary>
    /// <param name="encounter">The encounter entry.</param>
    /// <returns>The localized level text.</returns>
    private string FormatEncounterRange(AreaEncounterEntryPayload encounter)
    {
        return encounter.MinimumLevel == encounter.MaximumLevel
            ? Text["Lookup.Areas.Level", encounter.MinimumLevel]
            : Text["Lookup.Areas.LevelRange", encounter.MinimumLevel, encounter.MaximumLevel];
    }

    /// <summary>
    /// Gets the disclosed encounter identity with a stable identifier fallback.
    /// </summary>
    /// <param name="encounter">The disclosed encounter entry.</param>
    /// <returns>The species name or stable species identifier.</returns>
    private static string GetEncounterIdentity(AreaEncounterEntryPayload encounter)
        => encounter.SpeciesName ?? encounter.SpeciesId ?? "—";

    /// <summary>
    /// Gets the locally loaded icon for a disclosed encounter.
    /// </summary>
    /// <param name="encounter">The disclosed encounter entry.</param>
    /// <returns>The WebView sprite source, or null.</returns>
    private string? GetEncounterSpriteSource(AreaEncounterEntryPayload encounter)
        => _encounterSpriteSources.GetValueOrDefault(encounter.EntryId);

    /// <summary>
    /// Builds the local sprite-cache key for a disclosed trainer party slot.
    /// </summary>
    /// <param name="trainer">The owning trainer entry.</param>
    /// <param name="pokemon">The disclosed party slot.</param>
    /// <returns>The stable in-memory cache key.</returns>
    private static string GetTrainerPokemonKey(AreaTrainerEntryPayload trainer, AreaTrainerPokemonPayload pokemon)
        => $"{trainer.EntryId}:{pokemon.Slot}";

    /// <summary>
    /// Gets the locally loaded icon for a disclosed trainer party slot.
    /// </summary>
    /// <param name="trainer">The owning trainer entry.</param>
    /// <param name="pokemon">The disclosed party slot.</param>
    /// <returns>The WebView sprite source, or null.</returns>
    private string? GetTrainerSpriteSource(AreaTrainerEntryPayload trainer, AreaTrainerPokemonPayload pokemon)
        => _trainerSpriteSources.GetValueOrDefault(GetTrainerPokemonKey(trainer, pokemon));

    /// <summary>
    /// Opens the enlarged local sprite for one disclosed encounter.
    /// </summary>
    /// <param name="encounter">The disclosed encounter entry.</param>
    private void OpenEncounterSprite(AreaEncounterEntryPayload encounter)
    {
        OpenSprite(GetEncounterSpriteSource(encounter), GetEncounterIdentity(encounter));
    }

    /// <summary>
    /// Opens the enlarged sprite dialog.
    /// </summary>
    /// <param name="source">The loaded sprite source.</param>
    /// <param name="label">The accessible Pokemon label.</param>
    private void OpenSprite(string? source, string label)
    {
        if (source is null)
            return;

        _enlargedSpriteSource = source;
        _enlargedSpriteLabel = label;
    }

    /// <summary>
    /// Closes the enlarged Pokemon sprite.
    /// </summary>
    private void CloseSprite()
    {
        _enlargedSpriteSource = null;
        _enlargedSpriteLabel = null;
    }

    /// <summary>
    /// Closes the enlarged sprite when Escape is pressed.
    /// </summary>
    /// <param name="args">The keyboard event.</param>
    private void HandleSpriteDialogKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
            CloseSprite();
    }

    /// <summary>
    /// Gets whether an exception represents an expected lookup request failure.
    /// </summary>
    /// <param name="exception">The request exception.</param>
    /// <returns>Whether the exception can be displayed in the lookup interface.</returns>
    private static bool IsExpectedRequestException(Exception exception)
        => exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException;

    /// <summary>
    /// Removes tracker discovery subscriptions when the component is disposed.
    /// </summary>
    public void Dispose()
    {
        Discoveries.Changed -= HandleDiscoveryChanged;
        AccessService.Changed -= HandleDiagnosticAccessChanged;
        _summaryRequests.Dispose();
        _detailRequests.Dispose();
    }
}
