using Ironmon.Tracker.Connection.Obtainability;

namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Provides validated and cached game requests over the active tracker session.
/// </summary>
public sealed class TrackerRequestClient
{
    private readonly Lock _archiveObtainabilitySync = new();
    private readonly TrackerAreaRequestClient _areaRequests;
    private readonly TrackerDiagnosticAuthorizer _authorization;
    private readonly TrackerResponseCache _cache = new();
    private readonly TrackerCompletedRunRequestClient _completedRunRequests;
    private readonly TrackerDiagnosticRequestClient _diagnosticRequests;
    private readonly TrackerRunPreparationClient _runPreparation;
    private readonly SemaphoreSlim _obtainabilityRequestGate = new(1, 1);
    private readonly TrackerConnectionOptions _options;
    private readonly TrackerRequestSession _session;
    private readonly TrackerConnectionState _state;
    private readonly PlayerFusionMappingCoordinator _fusionMappings = new();
    private string? _selectedArchiveObtainabilityRunId;

    /// <summary>
    /// Occurs when the game reports a queued, started, or failed seeded-run import transition.
    /// </summary>
    public event Action<SeededRunImportStatusPayload>? SeededRunImportStatusChanged;

    /// <summary>
    /// Initializes a request client for one connection service.
    /// </summary>
    /// <param name="session">The correlated request session.</param>
    /// <param name="options">The tracker connection options.</param>
    /// <param name="state">The shared connection state.</param>
    /// <param name="areaDiscoveries">The tracker-owned area discovery store.</param>
    /// <param name="diagnosticAccess">The current signed diagnostic access, when configured.</param>
    internal TrackerRequestClient(TrackerRequestSession session, TrackerConnectionOptions options, TrackerConnectionState state, AreaDiscoveryStore areaDiscoveries, DiagnosticAccessService? diagnosticAccess = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(areaDiscoveries);
        _session = session;
        _options = options;
        _state = state;
        _authorization = new TrackerDiagnosticAuthorizer(options, state, diagnosticAccess);
        _areaRequests = new TrackerAreaRequestClient(session, areaDiscoveries, _authorization, _cache, _fusionMappings);
        _completedRunRequests = new TrackerCompletedRunRequestClient(session, _cache, _fusionMappings);
        _diagnosticRequests = new TrackerDiagnosticRequestClient(session, _authorization, _fusionMappings);
        _runPreparation = new TrackerRunPreparationClient(session, _fusionMappings);
    }

    /// <summary>
    /// Gets whether both the tracker launch mode and connected game authorize debug access.
    /// </summary>
    public bool DebugAuthorized => _authorization.DebugAuthorized;

    /// <summary>
    /// Gets the shared active-run obtainability progress source.
    /// </summary>
    public TrackerObtainabilityProgressState ObtainabilityProgress { get; } = new();

    /// <summary>
    /// Gets whether an archived run currently owns background obtainability preparation.
    /// </summary>
    internal bool ArchiveObtainabilityPrecalculationSelected
    {
        get
        {
            lock (_archiveObtainabilitySync)
                return _selectedArchiveObtainabilityRunId is not null;
        }
    }

    /// <summary>
    /// Selects one archived run for background obtainability preparation.
    /// </summary>
    /// <param name="runId">The selected archived run identifier.</param>
    public void SelectArchiveObtainabilityPrecalculation(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        lock (_archiveObtainabilitySync)
            _selectedArchiveObtainabilityRunId = runId;

        ObtainabilityProgress.Begin(runId, TrackerObtainabilityProgressScope.ArchivedRun);
    }

    /// <summary>
    /// Releases one archived run from background obtainability preparation.
    /// </summary>
    /// <param name="runId">The archived run identifier being released.</param>
    public void ClearArchiveObtainabilityPrecalculation(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        bool cleared = false;
        lock (_archiveObtainabilitySync)
        {
            if (string.Equals(_selectedArchiveObtainabilityRunId, runId, StringComparison.Ordinal))
            {
                _selectedArchiveObtainabilityRunId = null;
                cleared = true;
            }
        }

        if (cleared)
            ObtainabilityProgress.Cancel(runId, TrackerObtainabilityProgressScope.ArchivedRun);
    }

    /// <summary>
    /// Determines whether one named diagnostic capability is effective for the connected game.
    /// </summary>
    /// <param name="capability">The stable capability identifier.</param>
    /// <returns>Whether legacy development access or a negotiated grant authorizes the capability.</returns>
    public bool HasDiagnosticCapability(string capability)
        => _authorization.HasCapability(capability);

    /// <summary>
    /// Gets the token capabilities supported by one connected game in stable order.
    /// </summary>
    /// <param name="game">The connected game handshake.</param>
    /// <returns>The negotiated capability identifiers.</returns>
    internal IReadOnlyList<string> GetNegotiatedDiagnosticCapabilities(GameHandshakePayload game)
        => _authorization.GetNegotiatedCapabilities(game);

    /// <summary>
    /// Starts tracker-owned fusion assignment preparation from recovered active-run metadata.
    /// </summary>
    /// <param name="state">The recovered authoritative game state.</param>
    internal void PrepareActiveFusionAssignments(GameCurrentStatePayload state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!IsActiveFusionAssignmentPreparationEligible(state))
            return;

        _fusionMappings.PrepareActiveFusionAssignments(state.RunId!, state.FusionAssignments!);
    }

    /// <summary>
    /// Determines whether recovered state may start tracker-owned active-run assignment work.
    /// </summary>
    /// <param name="state">The recovered authoritative game state.</param>
    /// <returns>True when the run is ready and includes its assignment recipe; otherwise false.</returns>
    internal static bool IsActiveFusionAssignmentPreparationEligible(GameCurrentStatePayload state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.ActiveRunPreparationReady
            && !string.IsNullOrWhiteSpace(state.RunId)
            && state.FusionAssignments is not null;
    }

    /// <summary>
    /// Updates tracker-owned settings in the connected game.
    /// </summary>
    /// <param name="settings">The complete current tracker settings.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>A task containing the settings accepted by the game.</returns>
    public Task<TrackerSettingsPayload> UpdateSettingsAsync(TrackerSettingsPayload settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _options.AutoSelectStarter = settings.AutoSelectStarter;
        _options.FavoriteSpeciesIds = settings.FavoriteSpeciesIds;
        return _session.SendAsync<TrackerSettingsPayload, TrackerSettingsPayload>(TrackerCommands.UpdateSettings, settings, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Requests the connected game to enter its guarded manual-reset flow.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>A task reporting whether the active game queued the reset.</returns>
    public Task<ResetRunResponsePayload> ResetRunAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, object?> request = [];
        return _session.SendAsync<Dictionary<string, object?>, ResetRunResponsePayload>(TrackerCommands.ResetRun, request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Requests the active run's deterministic recipe after an explicit export action.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>A task containing the active run's seed-token inputs.</returns>
    public Task<SeededRunExportPayload> ExportSeededRunAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, object?> request = [];
        return _session.SendAsync<Dictionary<string, object?>, SeededRunExportPayload>(TrackerCommands.ExportSeededRun, request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Requests a transactional new run from signature-verified seeded-run inputs.
    /// </summary>
    /// <param name="request">The normalized token inputs confirmed by the player.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The game's initial accepted or rejected lifecycle status.</returns>
    public Task<SeededRunImportStatusPayload> ImportSeededRunAsync(SeededRunImportRequestPayload request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _session.SendAsync<SeededRunImportRequestPayload, SeededRunImportStatusPayload>(TrackerCommands.ImportSeededRun, request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Publishes one game-owned seeded-run import lifecycle transition.
    /// </summary>
    /// <param name="status">The validated transition.</param>
    internal void PublishSeededRunImportStatus(SeededRunImportStatusPayload status)
    {
        ArgumentNullException.ThrowIfNull(status);
        SeededRunImportStatusChanged?.Invoke(status);
    }

    /// <summary>
    /// Requests one bag item as the active Pokemon's current battle action.
    /// </summary>
    /// <param name="request">The selected item, move, and optional opposing target.</param>
    /// <param name="battleId">The active battle identifier.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The game's authoritative acceptance result.</returns>
    public Task<BattleItemUseResponsePayload> UseBattleItemAsync(BattleItemUseRequestPayload request, string battleId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(battleId);
        if (request.MoveIndex is not null)
            ArgumentOutOfRangeException.ThrowIfNegative(request.MoveIndex.Value);

        return _session.SendAsync<BattleItemUseRequestPayload, BattleItemUseResponsePayload>(TrackerCommands.UseBattleItem, request, GetConnectedRunId(), cancellationToken, battleId);
    }

    /// <summary>
    /// Requests a starter using the identity of the scene the player reviewed.
    /// </summary>
    /// <param name="request">The selection identifier and revealed slot.</param>
    /// <param name="cancellationToken">The token that cancels waiting for the response.</param>
    /// <returns>The game's authoritative acceptance result.</returns>
    public Task<StarterSelectionResponsePayload> SelectStarterAsync(StarterSelectionRequestPayload request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SelectionId);
        ArgumentOutOfRangeException.ThrowIfNegative(request.Index);
        return _session.SendAsync<StarterSelectionRequestPayload, StarterSelectionResponsePayload>(TrackerCommands.SelectStarter, request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Searches normal Pokemon in the connected game for Favorite Clause suggestions.
    /// </summary>
    /// <param name="query">The name fragment entered by the user.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="limit">The maximum number of matches to return.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The matching normal Pokemon.</returns>
    public Task<PokemonSearchResponsePayload> SearchFavoritePokemonAsync(string query, int offset = 0, int limit = TrackerProtocol.DefaultSearchPageSize, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, TrackerProtocol.MinimumSearchPageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, TrackerProtocol.MaximumSearchPageSize);
        DebugPokemonSearchRequestPayload request = new() { Query = query.Trim(), Offset = offset, Limit = limit, NormalOnly = true };
        return _session.SendAsync<DebugPokemonSearchRequestPayload, PokemonSearchResponsePayload>(TrackerCommands.FavoritePokemonSearch, request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Requests compact public area totals for the active or one archived run.
    /// </summary>
    /// <param name="category">The selected content category.</param>
    /// <param name="recipe">The archived run recipe, or null for the active run.</param>
    /// <param name="forceRefresh">Whether to bypass a previously cached response.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The public area summaries.</returns>
    public Task<AreaLookupSummaryResponsePayload> GetAreaSummariesAsync(AreaContentCategory category, CompletedRunRecipePayload? recipe = null, bool forceRefresh = false, CancellationToken cancellationToken = default)
        => _areaRequests.GetSummariesAsync(category, recipe, GetConnectedRunId(), forceRefresh, cancellationToken, _state.Snapshot.CurrentState?.OverworldEncounters);

    /// <summary>
    /// Requests one lazily loaded public area category for the active or one archived run.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <param name="category">The requested category.</param>
    /// <param name="recipe">The archived run recipe, or null for the active run.</param>
    /// <param name="forceRefresh">Whether to bypass a previously cached response.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <param name="encounterEnvironment">The encounter environment to page, or null to request its index.</param>
    /// <param name="offset">The zero-based encounter-entry offset.</param>
    /// <param name="limit">The maximum number of encounter entries to return.</param>
    /// <returns>The requested area category.</returns>
    public Task<AreaLookupDetailResponsePayload> GetAreaDetailsAsync(string areaId, AreaContentCategory category, CompletedRunRecipePayload? recipe = null, bool forceRefresh = false, CancellationToken cancellationToken = default, string? encounterEnvironment = null, int offset = 0, int limit = TrackerProtocol.AreaLookupPageSize)
        => _areaRequests.GetDetailsAsync(areaId, category, recipe, GetConnectedRunId(), forceRefresh, encounterEnvironment, offset, limit, cancellationToken, _state.Snapshot.CurrentState?.OverworldEncounters);

    /// <summary>
    /// Searches the connected game for Pokémon names compatible with one completed-run recipe.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="query">The name fragment entered by the user.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="limit">The maximum number of matches to return.</param>
    /// <param name="normalOnly">Whether to restrict matches to normal species.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The matching stable Pokémon identifiers.</returns>
    public Task<PokemonSearchResponsePayload> SearchPokemonAsync(CompletedRunRecipePayload recipe, string query, int offset = 0, int limit = TrackerProtocol.DefaultSearchPageSize, bool normalOnly = false, CancellationToken cancellationToken = default)
        => _completedRunRequests.SearchPokemonAsync(recipe, query, offset, limit, normalOnly, cancellationToken);

    /// <summary>
    /// Requests complete deterministic information for one Pokémon in a completed run.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The selected stable species and form identifier.</param>
    /// <param name="section">The independently requested information section.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The reconstructed Pokémon information.</returns>
    public Task<PokemonLookupSnapshot> LookupPokemonAsync(CompletedRunRecipePayload recipe, string speciesId, PokemonLookupSection section = PokemonLookupSection.Overview, CancellationToken cancellationToken = default)
        => _completedRunRequests.LookupPokemonAsync(recipe, speciesId, section, cancellationToken);

    /// <summary>
    /// Advances and reads the shared run-specific Pokemon obtainability calculation.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The optional target whose witness should be returned.</param>
    /// <param name="speciesIds">The bounded identifiers whose proven membership should be returned.</param>
    /// <param name="evolutionEdgeKeys">The bounded evolution connections whose possible membership should be returned.</param>
    /// <param name="foreground">Whether the game should temporarily prioritize this run's calculation.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The current calculation progress and proven species set.</returns>
    public async Task<PokemonObtainabilityResponsePayload> AdvancePokemonObtainabilityAsync(CompletedRunRecipePayload recipe, string? speciesId = null, IReadOnlyList<string>? speciesIds = null, IReadOnlyList<string>? evolutionEdgeKeys = null, bool foreground = false, CancellationToken cancellationToken = default)
    {
        ObtainabilityProgress.Begin(recipe.RunId, TrackerObtainabilityProgressScope.ArchivedRun);
        await _obtainabilityRequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _completedRunRequests.AdvanceObtainabilityAsync(recipe, speciesId, speciesIds, evolutionEdgeKeys, foreground, response => ObtainabilityProgress.Report(recipe.RunId, TrackerObtainabilityProgressScope.ArchivedRun, response), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException or TrackerProtocolException)
        {
            ObtainabilityProgress.Fail(recipe.RunId, TrackerObtainabilityProgressScope.ArchivedRun, exception.Message);
            throw;
        }
        finally
        {
            _obtainabilityRequestGate.Release();
        }
    }

    /// <summary>
    /// Advances the full active-run preparation without requiring diagnostic permissions or revealing lookup results.
    /// </summary>
    /// <param name="foreground">Whether the user explicitly requested priority calculation.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The current preparation progress.</returns>
    public async Task<PokemonObtainabilityResponsePayload> AdvanceActiveRunPreparationAsync(bool foreground = false, CancellationToken cancellationToken = default)
    {
        string? runId = GetConnectedRunId();
        ObtainabilityProgress.Begin(runId, TrackerObtainabilityProgressScope.ActiveRun);
        await _obtainabilityRequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _runPreparation.AdvanceAsync(foreground, response => ObtainabilityProgress.Report(runId, TrackerObtainabilityProgressScope.ActiveRun, response), runId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException or TrackerProtocolException)
        {
            ObtainabilityProgress.Fail(runId, TrackerObtainabilityProgressScope.ActiveRun, exception.Message);
            throw;
        }
        finally
        {
            _obtainabilityRequestGate.Release();
        }
    }

    /// <summary>
    /// Advances and reads the authorized active run's shared Pokemon obtainability calculation.
    /// </summary>
    /// <param name="speciesId">The optional target whose witness should be returned.</param>
    /// <param name="speciesIds">The bounded identifiers whose proven membership should be returned.</param>
    /// <param name="evolutionEdgeKeys">The bounded evolution connections whose possible membership should be returned.</param>
    /// <param name="foreground">Whether the game should temporarily prioritize this run's calculation.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The current calculation progress and proven species set.</returns>
    public async Task<PokemonObtainabilityResponsePayload> AdvanceDebugPokemonObtainabilityAsync(string? speciesId = null, IReadOnlyList<string>? speciesIds = null, IReadOnlyList<string>? evolutionEdgeKeys = null, bool foreground = false, CancellationToken cancellationToken = default)
    {
        string? runId = GetConnectedRunId();
        ObtainabilityProgress.Begin(runId, TrackerObtainabilityProgressScope.ActiveRun);
        await _obtainabilityRequestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _diagnosticRequests.AdvanceObtainabilityAsync(speciesId, speciesIds, evolutionEdgeKeys, foreground, response => ObtainabilityProgress.Report(runId, TrackerObtainabilityProgressScope.ActiveRun, response), runId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException or TrackerProtocolException)
        {
            ObtainabilityProgress.Fail(runId, TrackerObtainabilityProgressScope.ActiveRun, exception.Message);
            throw;
        }
        finally
        {
            _obtainabilityRequestGate.Release();
        }
    }

    /// <summary>
    /// Requests one filtered page of valid evolution candidates for a completed run.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The source species identifier.</param>
    /// <param name="side">The normal or component-specific candidate list.</param>
    /// <param name="query">The optional candidate name filter.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested candidate page.</returns>
    public Task<EvolutionCandidateSearchResponsePayload> SearchEvolutionCandidatesAsync(CompletedRunRecipePayload recipe, string speciesId, EvolutionCandidateSide side, string query, int offset = 0, CancellationToken cancellationToken = default)
        => _completedRunRequests.SearchEvolutionCandidatesAsync(recipe, speciesId, side, query, offset, cancellationToken);

    /// <summary>
    /// Requests one progressive page of generated evolution predecessors for a completed run.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The target species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="limit">The maximum number of predecessors to return.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested predecessor page.</returns>
    public Task<EvolutionPredecessorSearchResponsePayload> SearchEvolutionPredecessorsAsync(CompletedRunRecipePayload recipe, string speciesId, int offset = 0, int limit = TrackerProtocol.EvolutionPredecessorPageSize, CancellationToken cancellationToken = default)
        => _completedRunRequests.SearchEvolutionPredecessorsAsync(recipe, speciesId, offset, limit, cancellationToken);

    /// <summary>
    /// Requests one page of normal material pairs which produce a completed-run fusion.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The fusion species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested material-pair page.</returns>
    public Task<FusionMaterialSearchResponsePayload> SearchFusionMaterialsAsync(CompletedRunRecipePayload recipe, string speciesId, int offset = 0, CancellationToken cancellationToken = default)
        => _completedRunRequests.SearchFusionMaterialsAsync(recipe, speciesId, offset, cancellationToken);

    /// <summary>
    /// Requests one page of wild occurrences for a completed-run Pokemon.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The generated species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested wild-occurrence page.</returns>
    public Task<WildOccurrenceSearchResponsePayload> SearchWildOccurrencesAsync(CompletedRunRecipePayload recipe, string speciesId, int offset = 0, CancellationToken cancellationToken = default)
        => _completedRunRequests.SearchWildOccurrencesAsync(recipe, speciesId, offset, cancellationToken);

    /// <summary>
    /// Requests one page of trainer occurrences for a completed-run Pokemon.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The generated species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested trainer-occurrence page.</returns>
    public Task<TrainerOccurrenceSearchResponsePayload> SearchTrainerOccurrencesAsync(CompletedRunRecipePayload recipe, string speciesId, int offset = 0, CancellationToken cancellationToken = default)
        => _completedRunRequests.SearchTrainerOccurrencesAsync(recipe, speciesId, offset, cancellationToken);

    /// <summary>
    /// Requests both deterministic Ironmon fusion orientations for two normal Pokémon.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="firstSpeciesId">The first normal species identifier.</param>
    /// <param name="secondSpeciesId">The second normal species identifier.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The distinct deterministic fusion outcomes.</returns>
    public Task<FusionPreviewResponsePayload> PreviewFusionAsync(CompletedRunRecipePayload recipe, string firstSpeciesId, string secondSpeciesId, CancellationToken cancellationToken = default)
        => _completedRunRequests.PreviewFusionAsync(recipe, firstSpeciesId, secondSpeciesId, cancellationToken);

    /// <summary>
    /// Requests the game-owned Ironmon inspector data for one current Pokémon.
    /// </summary>
    /// <param name="request">The Pokémon source selection.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The complete authorized inspector snapshot.</returns>
    public Task<DebugPokemonInspectorSnapshot> InspectPokemonAsync(DebugPokemonInspectionRequestPayload request, CancellationToken cancellationToken = default)
        => _diagnosticRequests.InspectPokemonAsync(request, GetConnectedRunId(), cancellationToken);

    /// <summary>
    /// Requests game-owned run and randomizer diagnostics.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The authorized run diagnostics.</returns>
    public Task<DebugRunDiagnosticsSnapshot> GetDebugRunDiagnosticsAsync(CancellationToken cancellationToken = default)
        => _diagnosticRequests.GetRunDiagnosticsAsync(GetConnectedRunId(), cancellationToken);

    /// <summary>
    /// Requests the currently authorized development state and all authorized option catalogs.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The current development-control state.</returns>
    public Task<DebugDevelopmentStateSnapshot> GetDebugDevelopmentStateAsync(CancellationToken cancellationToken = default)
        => GetDebugDevelopmentStateAsync(includeCatalogs: true, includeEvolutions: true, cancellationToken);

    /// <summary>
    /// Requests selected portions of the authorized development state.
    /// </summary>
    /// <param name="includeCatalogs">Whether the shared ability, move, and item catalogs are included.</param>
    /// <param name="includeEvolutions">Whether the current Pokémon's evolution and devolution options are included.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested development-control state.</returns>
    public Task<DebugDevelopmentStateSnapshot> GetDebugDevelopmentStateAsync(bool includeCatalogs, bool includeEvolutions, CancellationToken cancellationToken = default)
        => _diagnosticRequests.GetDevelopmentStateAsync(GetConnectedRunId(), includeCatalogs, includeEvolutions, cancellationToken);

    /// <summary>
    /// Sends one individually authorized development action.
    /// </summary>
    /// <param name="request">The requested action and its input.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The authoritative current-player state after the action, without option catalogs.</returns>
    public Task<DebugDevelopmentStateSnapshot> ApplyDebugDevelopmentActionAsync(DebugDevelopmentActionRequestPayload request, CancellationToken cancellationToken = default)
        => _diagnosticRequests.ApplyDevelopmentActionAsync(request, GetConnectedRunId(), cancellationToken);

    /// <summary>
    /// Searches generated Pokémon in the active run through the authorized debug channel.
    /// </summary>
    /// <param name="query">The name fragment entered by the user.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="limit">The maximum number of matches to return.</param>
    /// <param name="normalOnly">Whether to restrict matches to normal species.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The matching active-run Pokémon identifiers.</returns>
    public Task<PokemonSearchResponsePayload> SearchDebugPokemonAsync(string query, int offset = 0, int limit = TrackerProtocol.DefaultSearchPageSize, bool normalOnly = false, CancellationToken cancellationToken = default)
        => _diagnosticRequests.SearchPokemonAsync(query, offset, limit, normalOnly, GetConnectedRunId(), cancellationToken);

    /// <summary>
    /// Requests generated information for one Pokémon in the active debug run.
    /// </summary>
    /// <param name="speciesId">The selected stable species and form identifier.</param>
    /// <param name="section">The independently requested information section.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The active-run generated Pokémon information.</returns>
    public async Task<PokemonLookupSnapshot> LookupDebugPokemonAsync(string speciesId, PokemonLookupSection section = PokemonLookupSection.Overview, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        cancellationToken.ThrowIfCancellationRequested();
        _authorization.EnsurePokemonLookup(section);
        string? runId = GetConnectedRunId();
        GameHandshakePayload? game = _state.Snapshot.Game;
        string? cacheKey = GetActiveLookupCacheKey(runId, speciesId, section);
        if (cacheKey is not null && _cache.TryGet(TrackerCommands.DebugPokemonLookup, cacheKey, out PokemonLookupSnapshot cached))
            return cached;

        PokemonLookupSnapshot response = await _diagnosticRequests.LookupPokemonAsync(speciesId, section, runId, cancellationToken).ConfigureAwait(false);
        if (cacheKey is not null
            && ReferenceEquals(game, _state.Snapshot.Game)
            && runId == GetConnectedRunId()
            && cacheKey == GetActiveLookupCacheKey(runId, speciesId, section)
            && response.Identity.Obtainability.Status != PokemonObtainabilityStatus.Calculating
            && (response.Overview is null || (response.Overview.TrainerOccurrences.Matches.Count == 0 && response.Overview.WildOccurrences.Matches.Count == 0)))
        {
            _cache.Set(TrackerCommands.DebugPokemonLookup, cacheKey, response);
        }

        return response;
    }

    /// <summary>
    /// Identifies reusable generated sections only after this active run's preparation completes.
    /// </summary>
    /// <param name="runId">The connected active run identifier.</param>
    /// <param name="speciesId">The selected stable species and form identifier.</param>
    /// <param name="section">The independently requested information section.</param>
    /// <returns>A run, encounter-mode, and access-specific cache key, or null while unavailable.</returns>
    private string? GetActiveLookupCacheKey(string? runId, string speciesId, PokemonLookupSection section)
    {
        TrackerConnectionSnapshot connection = _state.Snapshot;
        if (string.IsNullOrWhiteSpace(runId)
            || connection.Status != TrackerConnectionStatus.Connected
            || connection.Game is null
            || !(connection.CurrentState?.IronmonActive ?? connection.Game.IronmonActive)
            || connection.CurrentState?.CompletedRun is not null
            || ObtainabilityProgress.GetActiveRunSnapshot(runId).Status != TrackerObtainabilityProgressStatus.Complete)
        {
            return null;
        }

        string capabilities = string.Join('|', _authorization.GetNegotiatedCapabilities(connection.Game));
        return $"{runId}|{speciesId.ToUpperInvariant()}|{section}|{_authorization.DebugAuthorized}|{capabilities}|{connection.CurrentState?.OverworldEncounters}";
    }

    /// <summary>
    /// Requests one filtered page of valid evolution candidates from the active debug run.
    /// </summary>
    /// <param name="speciesId">The source species identifier.</param>
    /// <param name="side">The normal or component-specific candidate list.</param>
    /// <param name="query">The optional candidate name filter.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="target">The optional live source that securely supplies the represented species.</param>
    /// <param name="enemyPosition">The enemy battler position when the live source is an enemy.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested candidate page.</returns>
    public Task<EvolutionCandidateSearchResponsePayload> SearchDebugEvolutionCandidatesAsync(string speciesId, EvolutionCandidateSide side, string query, int offset = 0, DebugPokemonTarget? target = null, int? enemyPosition = null, CancellationToken cancellationToken = default)
        => _diagnosticRequests.SearchEvolutionCandidatesAsync(speciesId, side, query, offset, target, enemyPosition, GetConnectedRunId(), cancellationToken);

    /// <summary>
    /// Requests one progressive page of generated evolution predecessors from the authorized active run.
    /// </summary>
    /// <param name="speciesId">The target species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="limit">The maximum number of predecessors to return.</param>
    /// <param name="target">The optional live source that securely supplies the represented species.</param>
    /// <param name="enemyPosition">The enemy battler position when the live source is an enemy.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested predecessor page.</returns>
    public Task<EvolutionPredecessorSearchResponsePayload> SearchDebugEvolutionPredecessorsAsync(string speciesId, int offset = 0, int limit = TrackerProtocol.EvolutionPredecessorPageSize, DebugPokemonTarget? target = null, int? enemyPosition = null, CancellationToken cancellationToken = default)
        => _diagnosticRequests.SearchEvolutionPredecessorsAsync(speciesId, offset, limit, target, enemyPosition, GetConnectedRunId(), cancellationToken);

    /// <summary>
    /// Requests one page of fusion-material pairs from the authorized active run.
    /// </summary>
    /// <param name="speciesId">The fusion species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="target">The optional live source that securely supplies the represented species.</param>
    /// <param name="enemyPosition">The enemy battler position when the live source is an enemy.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested material-pair page.</returns>
    public Task<FusionMaterialSearchResponsePayload> SearchDebugFusionMaterialsAsync(string speciesId, int offset = 0, DebugPokemonTarget? target = null, int? enemyPosition = null, CancellationToken cancellationToken = default)
        => _diagnosticRequests.SearchFusionMaterialsAsync(speciesId, offset, target, enemyPosition, GetConnectedRunId(), cancellationToken);

    /// <summary>
    /// Requests one page of wild occurrences from the authorized active run.
    /// </summary>
    /// <param name="speciesId">The generated species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="target">The optional live source that securely supplies the represented species.</param>
    /// <param name="enemyPosition">The enemy battler position when the live source is an enemy.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested wild-occurrence page.</returns>
    public Task<WildOccurrenceSearchResponsePayload> SearchDebugWildOccurrencesAsync(string speciesId, int offset = 0, DebugPokemonTarget? target = null, int? enemyPosition = null, CancellationToken cancellationToken = default)
        => _diagnosticRequests.SearchWildOccurrencesAsync(speciesId, offset, target, enemyPosition, GetConnectedRunId(), cancellationToken);

    /// <summary>
    /// Requests one page of trainer occurrences from the authorized active run.
    /// </summary>
    /// <param name="speciesId">The generated species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="target">The optional live source that securely supplies the represented species.</param>
    /// <param name="enemyPosition">The enemy battler position when the live source is an enemy.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested trainer-occurrence page.</returns>
    public Task<TrainerOccurrenceSearchResponsePayload> SearchDebugTrainerOccurrencesAsync(string speciesId, int offset = 0, DebugPokemonTarget? target = null, int? enemyPosition = null, CancellationToken cancellationToken = default)
        => _diagnosticRequests.SearchTrainerOccurrencesAsync(speciesId, offset, target, enemyPosition, GetConnectedRunId(), cancellationToken);

    /// <summary>
    /// Requests both generated fusion orientations for the active debug run.
    /// </summary>
    /// <param name="firstSpeciesId">The first normal fusion material.</param>
    /// <param name="secondSpeciesId">The second normal fusion material.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The active-run fusion outcomes.</returns>
    public Task<FusionPreviewResponsePayload> PreviewDebugFusionAsync(string firstSpeciesId, string secondSpeciesId, CancellationToken cancellationToken = default)
        => _diagnosticRequests.PreviewFusionAsync(firstSpeciesId, secondSpeciesId, GetConnectedRunId(), cancellationToken);

    /// <summary>
    /// Clears connection-scoped caches and disconnects the underlying request session.
    /// </summary>
    internal void Disconnect()
    {
        _session.Disconnect();
        _cache.Clear();
    }

    /// <summary>
    /// Gets the active run identifier when reconnect recovery has provided one.
    /// </summary>
    /// <returns>The connected run identifier when available.</returns>
    private string? GetConnectedRunId()
        => _state.Snapshot.CurrentState?.RunId ?? _state.Snapshot.Game?.RunId;

}
