using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Connection.Obtainability;

namespace Ironmon.Tracker.Connection.Areas;

/// <summary>
/// Coordinates cached area requests with tracker-owned discovery persistence and archived restoration.
/// </summary>
internal sealed class TrackerAreaRequestClient
{
    private const int _preparationPollMilliseconds = 100;
    private readonly TrackerDiagnosticAuthorizer _authorization;
    private readonly TrackerResponseCache _cache;
    private readonly AreaDiscoveryStore _discoveries;
    private readonly TrackerRequestSession _session;
    private readonly PlayerFusionMappingCoordinator _fusionMappings;
    private const string _activeSource = "active";
    private const string _archivedSource = "archive";

    /// <summary>
    /// Initializes area request orchestration for one tracker connection.
    /// </summary>
    /// <param name="session">The correlated request session.</param>
    /// <param name="discoveries">The tracker-owned area discovery store.</param>
    /// <param name="authorization">The diagnostic authorization policy.</param>
    /// <param name="cache">The connection-scoped response cache.</param>
    /// <param name="fusionMappings">The connection's shared native fusion worker.</param>
    internal TrackerAreaRequestClient(TrackerRequestSession session, AreaDiscoveryStore discoveries, TrackerDiagnosticAuthorizer authorization, TrackerResponseCache cache, PlayerFusionMappingCoordinator fusionMappings)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(discoveries);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(fusionMappings);
        _session = session;
        _discoveries = discoveries;
        _authorization = authorization;
        _cache = cache;
        _fusionMappings = fusionMappings;
    }

    /// <summary>
    /// Requests compact public area totals for the active or one archived run.
    /// </summary>
    /// <param name="category">The selected content category.</param>
    /// <param name="recipe">The archived run recipe, or null for the active run.</param>
    /// <param name="connectedRunId">The active connected run identifier when available.</param>
    /// <param name="forceRefresh">Whether to bypass a previously cached response.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <param name="overworldEncounters">The active encounter mode, or null for archived totals.</param>
    /// <returns>The public area summaries.</returns>
    internal async Task<AreaLookupSummaryResponsePayload> GetSummariesAsync(AreaContentCategory category, CompletedRunRecipePayload? recipe, string? connectedRunId, bool forceRefresh, CancellationToken cancellationToken, bool? overworldEncounters)
    {
        string runId = recipe?.RunId ?? connectedRunId ?? throw new InvalidOperationException("No Ironmon run is connected.");
        long revision = _discoveries.GetRevision(runId);
        string source = recipe is null ? _activeSource : _archivedSource;
        bool? mode = recipe is null ? overworldEncounters : null;
        string cacheKey = $"{source}|{runId}|{revision}|{category}|{mode}";
        if (!forceRefresh && _cache.TryGet(TrackerCommands.AreaLookupSummary, cacheKey, out AreaLookupSummaryResponsePayload cached))
            return cached;

        AreaLookupSummaryRequestPayload request = new() { Recipe = recipe, Category = category };
        AreaLookupSummaryResponsePayload gameResponse = await _session.SendAsync<AreaLookupSummaryRequestPayload, AreaLookupSummaryResponsePayload>(TrackerCommands.AreaLookupSummary, request, runId, cancellationToken);
        bool? responseMode = recipe is null ? gameResponse.OverworldEncounters ?? mode : null;
        AreaLookupSummaryResponsePayload response = ApplyTrackerCounts(runId, category, gameResponse, responseMode);
        _cache.Set(TrackerCommands.AreaLookupSummary, $"{source}|{runId}|{response.Revision}|{category}|{responseMode}", response);
        return response;
    }

    /// <summary>
    /// Requests one lazily loaded public area category for the active or one archived run.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <param name="category">The requested category.</param>
    /// <param name="recipe">The archived run recipe, or null for the active run.</param>
    /// <param name="connectedRunId">The active connected run identifier when available.</param>
    /// <param name="forceRefresh">Whether to bypass a previously cached response.</param>
    /// <param name="encounterEnvironment">The encounter environment to page, or null to request its index.</param>
    /// <param name="offset">The zero-based encounter-entry offset.</param>
    /// <param name="limit">The maximum number of encounter entries to return.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <param name="overworldEncounters">The active encounter mode used to separate cached pages.</param>
    /// <returns>The requested area category.</returns>
    internal async Task<AreaLookupDetailResponsePayload> GetDetailsAsync(string areaId, AreaContentCategory category, CompletedRunRecipePayload? recipe, string? connectedRunId, bool forceRefresh, string? encounterEnvironment, int offset, int limit, CancellationToken cancellationToken, bool? overworldEncounters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(areaId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, TrackerProtocol.MinimumSearchPageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, TrackerProtocol.MaximumSearchPageSize);
        string runId = recipe?.RunId ?? connectedRunId ?? throw new InvalidOperationException("No Ironmon run is connected.");
        long revision = _discoveries.GetRevision(runId);
        string source = recipe is null ? _activeSource : _archivedSource;
        bool diagnosticAccess = recipe is null && _authorization.HasAreaDetails(category);
        bool? mode = recipe is null ? overworldEncounters : null;
        string cacheKey = $"{source}|{runId}|{revision}|{diagnosticAccess}|{mode}|{category}|{areaId}|{encounterEnvironment}|{offset}|{limit}";
        if (!forceRefresh && _cache.TryGet(TrackerCommands.AreaLookupDetail, cacheKey, out AreaLookupDetailResponsePayload cached))
            return cached;

        bool native = category == AreaContentCategory.Encounter && _fusionMappings.CanMapAreaFusions(runId, recipe);
        AreaLookupDetailRequestPayload request = new() { AreaId = areaId, Category = category, Recipe = recipe, DiscoveryKeys = _discoveries.GetKeys(runId, areaId, category), EncounterEnvironment = encounterEnvironment, Offset = offset, Limit = limit, UseNativeFusionMapping = native };
        AreaLookupDetailResponsePayload gameResponse = await _session.SendAsync<AreaLookupDetailRequestPayload, AreaLookupDetailResponsePayload>(TrackerCommands.AreaLookupDetail, request, runId, cancellationToken);
        while (gameResponse.Pending)
        {
            if (native && gameResponse.RequiredFusionMaterials.Count > 0)
            {
                IReadOnlyList<AreaFusionResultPayload>? results = await _fusionMappings.MapAreaFusionsAsync(runId, recipe, gameResponse.RequiredFusionMaterials, cancellationToken);
                native = results is not null;
                request = new AreaLookupDetailRequestPayload
                {
                    AreaId = request.AreaId,
                    Category = request.Category,
                    Recipe = request.Recipe,
                    DiscoveryKeys = request.DiscoveryKeys,
                    EncounterEnvironment = request.EncounterEnvironment,
                    Offset = request.Offset,
                    Limit = request.Limit,
                    UseNativeFusionMapping = native,
                    FusionResults = results ?? []
                };
            }
            else
            {
                await Task.Delay(_preparationPollMilliseconds, cancellationToken);
            }

            gameResponse = await _session.SendAsync<AreaLookupDetailRequestPayload, AreaLookupDetailResponsePayload>(TrackerCommands.AreaLookupDetail, request, runId, cancellationToken);
        }

        if (recipe is null)
        {
            _discoveries.RecordDetails(runId, gameResponse);
        }
        else
        {
            bool itemReconstructionAvailable = recipe.ItemGenerator is not null
                || recipe.ItemMappings.Count > 0
                || recipe.TmMappings.Count > 0;
            gameResponse = _discoveries.RestoreArchivedDetails(runId, gameResponse, itemReconstructionAvailable);
        }

        AreaLookupDetailResponsePayload response = WithTrackerRevision(runId, gameResponse);
        bool? responseMode = recipe is null ? response.OverworldEncounters ?? mode : null;
        _cache.Set(TrackerCommands.AreaLookupDetail, $"{source}|{runId}|{response.Revision}|{diagnosticAccess}|{responseMode}|{category}|{areaId}|{encounterEnvironment}|{offset}|{limit}", response);
        return response;
    }

    /// <summary>
    /// Applies tracker-owned completed counts to one game-provided area list.
    /// </summary>
    /// <param name="runId">The owning run identifier.</param>
    /// <param name="category">The requested category.</param>
    /// <param name="response">The game-provided area list.</param>
    /// <param name="overworldEncounters">The actual active mode used for the response, or null for archives.</param>
    /// <returns>The tracker-counted area list.</returns>
    private AreaLookupSummaryResponsePayload ApplyTrackerCounts(string runId, AreaContentCategory category, AreaLookupSummaryResponsePayload response, bool? overworldEncounters)
    {
        AreaSummaryPayload[] areas = [.. response.Areas.Select(area => new AreaSummaryPayload
        {
            AreaId = area.AreaId,
            Name = area.Name,
            MapIds = area.MapIds,
            TrainerTotal = area.TrainerTotal,
            TrainerDefeated = category == AreaContentCategory.Trainer ? Math.Min(area.TrainerTotal, Math.Max(area.TrainerDefeated, _discoveries.GetCount(runId, area.AreaId, category))) : 0,
            EncounterTotal = area.EncounterTotal,
            Encountered = category == AreaContentCategory.Encounter ? Math.Min(area.EncounterTotal, _discoveries.GetCount(runId, area.AreaId, category, overworldEncounters)) : 0,
            ItemTotal = area.ItemTotal,
            ItemsCollected = category == AreaContentCategory.Item ? Math.Min(area.ItemTotal, Math.Max(area.ItemsCollected, _discoveries.GetCount(runId, area.AreaId, category))) : 0
        })];

        return new AreaLookupSummaryResponsePayload { SchemaVersion = response.SchemaVersion, Revision = _discoveries.GetRevision(runId), OverworldEncounters = overworldEncounters, Areas = areas };
    }

    /// <summary>
    /// Replaces a game response's revision with the tracker-owned persisted revision.
    /// </summary>
    /// <param name="runId">The owning run identifier.</param>
    /// <param name="response">The game detail response.</param>
    /// <returns>The response carrying the tracker revision.</returns>
    private AreaLookupDetailResponsePayload WithTrackerRevision(string runId, AreaLookupDetailResponsePayload response)
    {
        return new AreaLookupDetailResponsePayload
        {
            AreaId = response.AreaId,
            Name = response.Name,
            Category = response.Category,
            Revision = _discoveries.GetRevision(runId),
            Offset = response.Offset,
            Limit = response.Limit,
            TotalCount = response.TotalCount,
            Pending = response.Pending,
            OverworldEncounters = response.OverworldEncounters,
            EncounterEnvironment = response.EncounterEnvironment,
            EncounterEnvironments = response.EncounterEnvironments,
            Trainers = response.Trainers,
            Encounters = response.Encounters,
            EncounterFusions = response.EncounterFusions,
            Items = response.Items
        };
    }
}
