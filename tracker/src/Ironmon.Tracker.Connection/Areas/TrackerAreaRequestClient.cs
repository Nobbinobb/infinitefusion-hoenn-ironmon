using Ironmon.Tracker.Connection.Transport;

namespace Ironmon.Tracker.Connection.Areas;

/// <summary>
/// Coordinates cached area requests with tracker-owned discovery persistence and archived restoration.
/// </summary>
internal sealed class TrackerAreaRequestClient
{
    private readonly TrackerDiagnosticAuthorizer _authorization;
    private readonly TrackerResponseCache _cache;
    private readonly AreaDiscoveryStore _discoveries;
    private readonly TrackerRequestSession _session;

    /// <summary>
    /// Initializes area request orchestration for one tracker connection.
    /// </summary>
    /// <param name="session">The correlated request session.</param>
    /// <param name="discoveries">The tracker-owned area discovery store.</param>
    /// <param name="authorization">The diagnostic authorization policy.</param>
    /// <param name="cache">The connection-scoped response cache.</param>
    internal TrackerAreaRequestClient(TrackerRequestSession session, AreaDiscoveryStore discoveries, TrackerDiagnosticAuthorizer authorization, TrackerResponseCache cache)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(discoveries);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(cache);
        _session = session;
        _discoveries = discoveries;
        _authorization = authorization;
        _cache = cache;
    }

    /// <summary>
    /// Requests compact public area totals for the active or one archived run.
    /// </summary>
    /// <param name="category">The selected content category.</param>
    /// <param name="recipe">The archived run recipe, or null for the active run.</param>
    /// <param name="connectedRunId">The active connected run identifier when available.</param>
    /// <param name="forceRefresh">Whether to bypass a previously cached response.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The public area summaries.</returns>
    internal async Task<AreaLookupSummaryResponsePayload> GetSummariesAsync(AreaContentCategory category, CompletedRunRecipePayload? recipe, string? connectedRunId, bool forceRefresh, CancellationToken cancellationToken)
    {
        string runId = recipe?.RunId ?? connectedRunId ?? throw new InvalidOperationException("No Ironmon run is connected.");
        long revision = _discoveries.GetRevision(runId);
        string source = recipe is null ? "active" : "archive";
        string cacheKey = $"{source}|{runId}|{revision}|{category}";
        if (!forceRefresh && _cache.TryGet(TrackerCommands.AreaLookupSummary, cacheKey, out AreaLookupSummaryResponsePayload cached))
            return cached;

        AreaLookupSummaryRequestPayload request = new() { Recipe = recipe, Category = category };
        AreaLookupSummaryResponsePayload gameResponse = await _session.SendAsync<AreaLookupSummaryRequestPayload, AreaLookupSummaryResponsePayload>(TrackerCommands.AreaLookupSummary, request, runId, cancellationToken);
        AreaLookupSummaryResponsePayload response = ApplyTrackerCounts(runId, category, gameResponse);
        _cache.Set(TrackerCommands.AreaLookupSummary, $"{source}|{runId}|{response.Revision}|{category}", response);
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
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested area category.</returns>
    internal async Task<AreaLookupDetailResponsePayload> GetDetailsAsync(string areaId, AreaContentCategory category, CompletedRunRecipePayload? recipe, string? connectedRunId, bool forceRefresh, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(areaId);
        string runId = recipe?.RunId ?? connectedRunId ?? throw new InvalidOperationException("No Ironmon run is connected.");
        long revision = _discoveries.GetRevision(runId);
        string source = recipe is null ? "active" : "archive";
        bool diagnosticAccess = recipe is null && _authorization.HasAreaDetails(category);
        string cacheKey = $"{source}|{runId}|{revision}|{diagnosticAccess}|{category}|{areaId}";
        if (!forceRefresh && _cache.TryGet(TrackerCommands.AreaLookupDetail, cacheKey, out AreaLookupDetailResponsePayload cached))
            return cached;

        AreaLookupDetailRequestPayload request = new() { AreaId = areaId, Category = category, Recipe = recipe, DiscoveryKeys = _discoveries.GetKeys(runId, areaId, category) };
        AreaLookupDetailResponsePayload gameResponse = await _session.SendAsync<AreaLookupDetailRequestPayload, AreaLookupDetailResponsePayload>(TrackerCommands.AreaLookupDetail, request, runId, cancellationToken);
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
        _cache.Set(TrackerCommands.AreaLookupDetail, $"{source}|{runId}|{response.Revision}|{diagnosticAccess}|{category}|{areaId}", response);
        return response;
    }

    /// <summary>
    /// Applies tracker-owned completed counts to one game-provided area list.
    /// </summary>
    /// <param name="runId">The owning run identifier.</param>
    /// <param name="category">The requested category.</param>
    /// <param name="response">The game-provided area list.</param>
    /// <returns>The tracker-counted area list.</returns>
    private AreaLookupSummaryResponsePayload ApplyTrackerCounts(string runId, AreaContentCategory category, AreaLookupSummaryResponsePayload response)
    {
        AreaSummaryPayload[] areas = [.. response.Areas.Select(area => new AreaSummaryPayload
        {
            AreaId = area.AreaId,
            Name = area.Name,
            MapIds = area.MapIds,
            TrainerTotal = area.TrainerTotal,
            TrainerDefeated = category == AreaContentCategory.Trainer ? Math.Min(area.TrainerTotal, Math.Max(area.TrainerDefeated, _discoveries.GetCount(runId, area.AreaId, category))) : 0,
            EncounterTotal = area.EncounterTotal,
            Encountered = category == AreaContentCategory.Encounter ? Math.Min(area.EncounterTotal, _discoveries.GetCount(runId, area.AreaId, category)) : 0,
            ItemTotal = area.ItemTotal,
            ItemsCollected = category == AreaContentCategory.Item ? Math.Min(area.ItemTotal, Math.Max(area.ItemsCollected, _discoveries.GetCount(runId, area.AreaId, category))) : 0
        })];

        return new AreaLookupSummaryResponsePayload { SchemaVersion = response.SchemaVersion, Revision = _discoveries.GetRevision(runId), Areas = areas };
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
            Trainers = response.Trainers,
            Encounters = response.Encounters,
            Items = response.Items
        };
    }
}
