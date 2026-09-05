using Microsoft.AspNetCore.Components;
using Ironmon.Tracker.App.Components.Common;
using Microsoft.JSInterop;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents lazily loaded area progress for an active or archived run.
/// </summary>
public partial class AreaLookupExplorer : IDisposable
{
    private const int _pageSize = TrackerProtocol.AreaLookupPageSize;
    private const string _summaryRequestKey = "summary";
    private const string _grassEnvironment = "grass";
    private const string _caveEnvironment = "cave";
    private const string _waterEnvironment = "water";
    private const string _fishingEnvironment = "fishing";
    private const string _crossEnvironment = "cross";
    private const string _scrollFunction = "ironmonTrackerUi.scrollLookup";
    private ElementReference _lookupElement;
    private string? _pendingScrollKey;
    private static readonly AreaContentCategory[] Categories = [AreaContentCategory.Trainer, AreaContentCategory.Encounter, AreaContentCategory.Item];
    private readonly Dictionary<string, AreaLookupDetailResponsePayload> _details = [];
    private readonly Dictionary<string, PaginationState> _detailPagination = [];
    private readonly Dictionary<string, string> _detailErrors = [];
    private readonly HashSet<string> _expandedAreas = [];
    private readonly HashSet<string> _expandedEnvironments = [];
    private readonly HashSet<string> _loadingDetails = [];
    private readonly LatestRequestCoordinator<string> _detailRequests = new();
    private readonly LatestRequestCoordinator<string> _summaryRequests = new();
    private readonly PaginationState _areaPagination = new(_pageSize);
    private IReadOnlyList<AreaSummaryPayload> _areas = [];
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
    /// Gets the browser runtime used to keep the new page's section visible.
    /// </summary>
    [Inject]
    private IJSRuntime JavaScript { get; set; } = null!;

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
    /// Gets or sets whether to use the redesigned live presentation; archived views retain the legacy layout by default.
    /// The redesigned host supplies its own heading and category tabs.
    /// </summary>
    [Parameter]
    public bool Redesigned { get; set; }

    /// <summary>
    /// Subscribes to tracker-owned discoveries received regardless of the visible page.
    /// </summary>
    protected override void OnInitialized()
    {
        Discoveries.Changed += HandleDiscoveryChanged;
        AccessService.Changed += HandleDiagnosticAccessChanged;
    }

    /// <summary>
    /// Returns the changed page to the beginning of its area or encounter environment.
    /// </summary>
    /// <param name="firstRender">Whether the component has just been rendered for the first time.</param>
    /// <returns>A task representing optional scroll adjustment.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!Redesigned || _pendingScrollKey is null)
            return;

        string key = _pendingScrollKey;
        _pendingScrollKey = null;
        await JavaScript.InvokeVoidAsync(_scrollFunction, _lookupElement, key);
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
            _expandedEnvironments.Clear();
        }
        else if (categoryChanged)
        {
            ResetPendingRequests();
        }

        _selectedCategory = desiredCategory;
        _areaPagination.Reset();
        _detailPagination.Clear();
        _expandedAreas.Clear();
        _expandedEnvironments.Clear();
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
        _expandedEnvironments.Clear();
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
    /// <param name="encounterEnvironment">The encounter environment to page, or null to load its index.</param>
    /// <param name="requestedPage">The page to select only after its response succeeds, or null to refresh the current page.</param>
    /// <returns>A task representing detail loading.</returns>
    private async Task LoadAreaDetailsAsync(string areaId, bool forceRefresh, string? encounterEnvironment = null, int? requestedPage = null)
    {
        string key = GetDetailKey(areaId, encounterEnvironment);
        AreaContentCategory category = _selectedCategory;
        CompletedRunRecipePayload? recipe = Recipe;
        LatestRequestLease<string> request = _detailRequests.Begin(key);
        _loadingDetails.Add(key);
        _detailErrors.Remove(key);
        try
        {
            int offset = category == AreaContentCategory.Encounter && encounterEnvironment is not null ? (requestedPage ?? GetDetailPagination(areaId, encounterEnvironment).PageIndex) * _pageSize : 0;
            AreaLookupDetailResponsePayload response = await Connection.GetAreaDetailsAsync(areaId, category, recipe, forceRefresh, request.CancellationToken, encounterEnvironment, offset, _pageSize);
            if (!request.IsCurrent)
                return;

            _details[key] = response;
            if (requestedPage is int page)
            {
                GetDetailPagination(areaId, encounterEnvironment).Select(page);
                _pendingScrollKey = key;
            }

            if (category != AreaContentCategory.Encounter || encounterEnvironment is not null)
                GetDetailPagination(areaId, encounterEnvironment).Clamp(GetDetailEntryCount(response));
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
            if (args.Category == _selectedCategory && _expandedAreas.Contains(args.AreaId) && !_loadingDetails.Contains(GetDetailKey(args.AreaId)))
            {
                RemoveEncounterEnvironmentDetails(args.AreaId);
                await LoadAreaDetailsAsync(args.AreaId, true);
                if (_selectedCategory == AreaContentCategory.Encounter && _details.TryGetValue(GetDetailKey(args.AreaId), out AreaLookupDetailResponsePayload? index))
                {
                    foreach (AreaEncounterEnvironmentPayload environment in index.EncounterEnvironments)
                    {
                        if (IsEnvironmentExpanded(args.AreaId, environment.Key))
                            await LoadAreaDetailsAsync(args.AreaId, true, environment.Key);
                    }
                }
            }

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
        _expandedAreas.Clear();
        _expandedEnvironments.Clear();
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
    {
        return GetDetailPagination(areaId).GetPage(entries);
    }

    /// <summary>
    /// Selects the previous area-summary page.
    /// </summary>
    private void PreviousAreaPage()
    {
        _areaPagination.Previous();
        _pendingScrollKey = string.Empty;
    }

    /// <summary>
    /// Selects the next area-summary page.
    /// </summary>
    /// <param name="areaCount">The total visible area count.</param>
    private void NextAreaPage(int areaCount)
    {
        _areaPagination.Next(areaCount);
        _pendingScrollKey = string.Empty;
    }

    /// <summary>
    /// Selects the previous entry page for one expanded area.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <param name="encounterEnvironment">The encounter environment being paged, or null for a local category page.</param>
    private async Task PreviousDetailPage(string areaId, string? encounterEnvironment = null)
    {
        string key = GetDetailKey(areaId, encounterEnvironment);
        PaginationState pagination = GetDetailPagination(areaId, encounterEnvironment);
        if (_loadingDetails.Contains(key) || !pagination.HasPrevious)
            return;

        if (_selectedCategory == AreaContentCategory.Encounter)
        {
            await LoadAreaDetailsAsync(areaId, false, encounterEnvironment, pagination.PageIndex - 1);
        }
        else
        {
            pagination.Previous();
            _pendingScrollKey = key;
        }
    }

    /// <summary>
    /// Selects the next entry page for one expanded area.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <param name="entryCount">The total entry count.</param>
    /// <param name="encounterEnvironment">The encounter environment being paged, or null for a local category page.</param>
    private async Task NextDetailPage(string areaId, int entryCount, string? encounterEnvironment = null)
    {
        string key = GetDetailKey(areaId, encounterEnvironment);
        PaginationState pagination = GetDetailPagination(areaId, encounterEnvironment);
        if (_loadingDetails.Contains(key) || !pagination.HasNext(entryCount))
            return;

        if (_selectedCategory == AreaContentCategory.Encounter)
        {
            await LoadAreaDetailsAsync(areaId, false, encounterEnvironment, pagination.PageIndex + 1);
        }
        else
        {
            pagination.Next(entryCount);
            _pendingScrollKey = key;
        }
    }

    /// <summary>
    /// Gets or creates pagination for one area and selected category.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <param name="encounterEnvironment">The encounter environment being paged, or null for the category page.</param>
    /// <returns>The area's detail pagination.</returns>
    private PaginationState GetDetailPagination(string areaId, string? encounterEnvironment = null)
    {
        string key = GetDetailKey(areaId, encounterEnvironment);
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
        AreaContentCategory.Trainer => detail.TotalCount > 0 ? detail.TotalCount : detail.Trainers.Count,
        AreaContentCategory.Encounter => detail.TotalCount > 0 ? detail.TotalCount : detail.Encounters.Count,
        AreaContentCategory.Item => detail.TotalCount > 0 ? detail.TotalCount : detail.Items.Count,
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
    /// Expands or collapses one environment within an area.
    /// </summary>
    /// <param name="areaId">The stable area identifier.</param>
    /// <param name="environment">The stable environment key.</param>
    /// <returns>A task representing optional environment loading.</returns>
    private async Task ToggleEnvironmentAsync(string areaId, string environment)
    {
        string key = GetEnvironmentKey(areaId, environment);
        if (!_expandedEnvironments.Add(key))
        {
            _expandedEnvironments.Remove(key);
            return;
        }

        string detailKey = GetDetailKey(areaId, environment);
        if (!_details.ContainsKey(detailKey) && !_loadingDetails.Contains(detailKey))
            await LoadAreaDetailsAsync(areaId, Recipe is null, environment);
    }

    /// <summary>
    /// Removes environment pages whose possible-fusion set may have changed after a discovery.
    /// </summary>
    /// <param name="areaId">The stable area identifier.</param>
    private void RemoveEncounterEnvironmentDetails(string areaId)
    {
        string indexKey = GetDetailKey(areaId);
        string[] keys = [.. _details.Keys.Where(key => key.StartsWith(indexKey, StringComparison.Ordinal) && key != indexKey)];
        foreach (string key in keys)
        {
            _details.Remove(key);
            _detailErrors.Remove(key);
        }
    }

    /// <summary>
    /// Gets whether one environment is expanded.
    /// </summary>
    /// <param name="areaId">The stable area identifier.</param>
    /// <param name="environment">The stable environment key.</param>
    /// <returns>Whether the environment is expanded.</returns>
    private bool IsEnvironmentExpanded(string areaId, string environment)
        => _expandedEnvironments.Contains(GetEnvironmentKey(areaId, environment));

    /// <summary>
    /// Builds the expansion key for one area environment.
    /// </summary>
    /// <param name="areaId">The stable area identifier.</param>
    /// <param name="environment">The stable environment key.</param>
    /// <returns>The expansion key.</returns>
    private string GetEnvironmentKey(string areaId, string environment)
        => $"{SourceKey}:{areaId}:{environment}";

    /// <summary>
    /// Groups one environment page into ordered, non-collapsible subsections.
    /// </summary>
    /// <param name="detail">The current area detail.</param>
    /// <returns>The page's plain ordered subsections.</returns>
    private IReadOnlyList<EncounterSubsection> GetEncounterSubsections(AreaLookupDetailResponsePayload detail)
    {
        List<EncounterSubsection> subsections = [.. detail.Encounters
            .GroupBy(entry => entry.EncounterType, StringComparer.Ordinal)
            .Select(group => new EncounterSubsection(group.Key, [.. group], []))];

        if (detail.EncounterFusions.Count > 0)
            subsections.Add(new EncounterSubsection(Text["Lookup.Areas.Fusions"], [], detail.EncounterFusions));


        return subsections;
    }

    /// <summary>
    /// Gets a localized broad environment name.
    /// </summary>
    /// <param name="environment">The stable environment key.</param>
    /// <returns>The localized name.</returns>
    private string GetEnvironmentName(string environment) => environment switch
    {
        _grassEnvironment => Text["Lookup.Areas.Environment.Grass"],
        _caveEnvironment => Text["Lookup.Areas.Environment.Cave"],
        _waterEnvironment => Text["Lookup.Areas.Environment.Water"],
        _fishingEnvironment => Text["Lookup.Areas.Environment.Fishing"],
        _crossEnvironment => Text["Lookup.Areas.Environment.Cross"],
        _ => Text["Lookup.Areas.Environment.Special"]
    };

    /// <summary>
    /// Gets the outline icon for a broad encounter environment.
    /// </summary>
    /// <param name="environment">The stable environment key.</param>
    /// <returns>The environment's shared icon.</returns>
    private static ObsidianIconKind GetEnvironmentIcon(string environment) => environment switch
    {
        _grassEnvironment => ObsidianIconKind.Sprout,
        _caveEnvironment => ObsidianIconKind.Mountain,
        _waterEnvironment => ObsidianIconKind.Waves,
        _fishingEnvironment => ObsidianIconKind.Fish,
        _crossEnvironment => ObsidianIconKind.Merge,
        _ => ObsidianIconKind.Sparkles
    };

    /// <summary>
    /// Builds the in-memory detail key for the selected category and area.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <param name="encounterEnvironment">The encounter environment, or null for the category index.</param>
    /// <returns>The detail key.</returns>
    private string GetDetailKey(string areaId, string? encounterEnvironment = null)
        => $"{_selectedCategory}:{areaId}:{encounterEnvironment}";

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
    /// Formats a derived fusion's resulting level or inclusive range.
    /// </summary>
    /// <param name="fusion">The derived fusion entry.</param>
    /// <returns>The localized level text.</returns>
    private string FormatFusionRange(AreaEncounterFusionEntryPayload fusion)
    {
        return fusion.MinimumLevel == fusion.MaximumLevel
            ? Text["Lookup.Areas.Level", fusion.MinimumLevel]
            : Text["Lookup.Areas.LevelRange", fusion.MinimumLevel, fusion.MaximumLevel];
    }

    /// <summary>
    /// Formats both source slots for a derived fusion.
    /// </summary>
    /// <param name="fusion">The derived fusion entry.</param>
    /// <returns>The localized source description.</returns>
    private string FormatFusionSources(AreaEncounterFusionEntryPayload fusion)
        => Text["Lookup.Areas.FusionSources", fusion.FirstEncounterType, fusion.FirstSlot, fusion.SecondEncounterType, fusion.SecondSlot];

    /// <summary>
    /// Gets the localized fusion mechanic and conditional roll chance.
    /// </summary>
    /// <param name="fusion">The derived fusion entry.</param>
    /// <returns>The localized mechanic description.</returns>
    private string FormatFusionOrigin(AreaEncounterFusionEntryPayload fusion)
    {
        string mechanic = fusion.Origin.StartsWith("overworld", StringComparison.Ordinal)
            ? Text["Lookup.Areas.FusionOrigin.Overworld"]
            : Text["Lookup.Areas.FusionOrigin.Standard"];

        return Text["Lookup.Areas.FusionChance", mechanic, fusion.FusionChancePercent];
    }

    /// <summary>
    /// Gets the disclosed encounter identity with a stable identifier fallback.
    /// </summary>
    /// <param name="encounter">The disclosed encounter entry.</param>
    /// <returns>The species name or stable species identifier.</returns>
    private static string GetEncounterIdentity(AreaEncounterEntryPayload encounter)
        => encounter.SpeciesName ?? encounter.SpeciesId ?? "—";

    /// <summary>
    /// Gets whether an exception represents an expected lookup request failure.
    /// </summary>
    /// <param name="exception">The request exception.</param>
    /// <returns>Whether the exception can be displayed in the lookup interface.</returns>
    private static bool IsExpectedRequestException(Exception exception)
        => exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException;

    /// <summary>
    /// Groups authored encounter slots or possible fusions under one plain subsection heading.
    /// </summary>
    /// <param name="Name">The subsection display name.</param>
    /// <param name="Encounters">The authored encounter slots in the subsection.</param>
    /// <param name="Fusions">The possible derived fusions in the subsection.</param>
    private sealed record EncounterSubsection(string Name, IReadOnlyList<AreaEncounterEntryPayload> Encounters, IReadOnlyList<AreaEncounterFusionEntryPayload> Fusions);

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
