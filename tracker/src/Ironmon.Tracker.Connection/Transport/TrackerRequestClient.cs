using System.Collections.Concurrent;

namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Provides validated and cached game requests over the active tracker session.
/// </summary>
public sealed class TrackerRequestClient
{
    private readonly ConcurrentDictionary<string, AreaLookupDetailResponsePayload> _areaDetailCache = new();
    private readonly ConcurrentDictionary<string, AreaLookupSummaryResponsePayload> _areaSummaryCache = new();
    private readonly ConcurrentDictionary<string, FusionPreviewResponsePayload> _fusionPreviewCache = new();
    private readonly ConcurrentDictionary<string, FusionMaterialSearchResponsePayload> _fusionMaterialCache = new();
    private readonly ConcurrentDictionary<string, PokemonLookupSnapshot> _pokemonLookupCache = new();
    private readonly ConcurrentDictionary<string, PokemonSearchResponsePayload> _pokemonSearchCache = new();
    private readonly ConcurrentDictionary<string, EvolutionCandidateSearchResponsePayload> _evolutionCandidateCache = new();
    private readonly ConcurrentDictionary<string, TrainerOccurrenceSearchResponsePayload> _trainerOccurrenceCache = new();
    private readonly ConcurrentDictionary<string, WildOccurrenceSearchResponsePayload> _wildOccurrenceCache = new();
    private readonly AreaDiscoveryStore _areaDiscoveries;
    private readonly DiagnosticAccessService? _diagnosticAccess;
    private readonly TrackerConnectionOptions _options;
    private readonly TrackerRequestSession _session;
    private readonly TrackerConnectionState _state;

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
        _areaDiscoveries = areaDiscoveries;
        _diagnosticAccess = diagnosticAccess;
    }

    /// <summary>
    /// Gets whether both the tracker launch mode and connected game authorize debug access.
    /// </summary>
    public bool DebugAuthorized => _options.DebugRequested && _state.Snapshot.Game?.DebugAvailable == true;

    /// <summary>
    /// Determines whether one named diagnostic capability is effective for the connected game.
    /// </summary>
    /// <param name="capability">The stable capability identifier.</param>
    /// <returns>Whether legacy development access or a negotiated grant authorizes the capability.</returns>
    public bool HasDiagnosticCapability(string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        if (DebugAuthorized)
            return true;

        GameHandshakePayload? game = _state.Snapshot.Game;
        DiagnosticAccessSnapshot? access = _diagnosticAccess?.Snapshot;
        return game is not null
            && access?.State != DiagnosticAccessState.DeveloperOverride
            && game.SupportedDiagnosticCapabilities.Contains(capability, StringComparer.Ordinal)
            && access?.HasCapability(capability) == true;
    }

    /// <summary>
    /// Gets the token capabilities supported by one connected game in stable order.
    /// </summary>
    /// <param name="game">The connected game handshake.</param>
    /// <returns>The negotiated capability identifiers.</returns>
    internal IReadOnlyList<string> GetNegotiatedDiagnosticCapabilities(GameHandshakePayload game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (_diagnosticAccess is null || (_diagnosticAccess.Snapshot.State == DiagnosticAccessState.DeveloperOverride && !(_options.DebugRequested && game.DebugAvailable)))
            return [];

        HashSet<string> supported = new(game.SupportedDiagnosticCapabilities, StringComparer.Ordinal);
        return [.. _diagnosticAccess.Snapshot.EffectiveCapabilities.Where(supported.Contains).Order(StringComparer.Ordinal)];
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
    public async Task<AreaLookupSummaryResponsePayload> GetAreaSummariesAsync(AreaContentCategory category, CompletedRunRecipePayload? recipe = null, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        string runId = recipe?.RunId ?? GetConnectedRunId() ?? throw new InvalidOperationException("No Ironmon run is connected.");
        long revision = _areaDiscoveries.GetRevision(runId);
        string source = recipe is null ? "active" : "archive";
        string cacheKey = $"{source}|{runId}|{revision}|{category}";
        if (!forceRefresh && _areaSummaryCache.TryGetValue(cacheKey, out AreaLookupSummaryResponsePayload? cached))
            return cached;

        AreaLookupSummaryRequestPayload request = new() { Recipe = recipe, Category = category };
        AreaLookupSummaryResponsePayload gameResponse = await _session.SendAsync<AreaLookupSummaryRequestPayload, AreaLookupSummaryResponsePayload>(TrackerCommands.AreaLookupSummary, request, runId, cancellationToken);
        AreaLookupSummaryResponsePayload response = ApplyTrackerCounts(runId, category, gameResponse);
        _areaSummaryCache[$"{source}|{runId}|{response.Revision}|{category}"] = response;
        return response;
    }

    /// <summary>
    /// Requests one lazily loaded public area category for the active or one archived run.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <param name="category">The requested category.</param>
    /// <param name="recipe">The archived run recipe, or null for the active run.</param>
    /// <param name="forceRefresh">Whether to bypass a previously cached response.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested area category.</returns>
    public async Task<AreaLookupDetailResponsePayload> GetAreaDetailsAsync(string areaId, AreaContentCategory category, CompletedRunRecipePayload? recipe = null, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(areaId);
        string runId = recipe?.RunId ?? GetConnectedRunId() ?? throw new InvalidOperationException("No Ironmon run is connected.");
        long revision = _areaDiscoveries.GetRevision(runId);
        string source = recipe is null ? "active" : "archive";
        bool diagnosticAccess = recipe is null && HasDiagnosticCapability(GetAreaCapability(category));
        string cacheKey = $"{source}|{runId}|{revision}|{diagnosticAccess}|{category}|{areaId}";
        if (!forceRefresh && _areaDetailCache.TryGetValue(cacheKey, out AreaLookupDetailResponsePayload? cached))
            return cached;

        AreaLookupDetailRequestPayload request = new() { AreaId = areaId, Category = category, Recipe = recipe, DiscoveryKeys = _areaDiscoveries.GetKeys(runId, areaId, category) };
        AreaLookupDetailResponsePayload gameResponse = await _session.SendAsync<AreaLookupDetailRequestPayload, AreaLookupDetailResponsePayload>(TrackerCommands.AreaLookupDetail, request, runId, cancellationToken);
        if (recipe is null)
        {
            _areaDiscoveries.RecordDetails(runId, gameResponse);
        }
        else
        {
            bool itemReconstructionAvailable = recipe.ItemGenerator is not null
                || recipe.ItemMappings.Count > 0
                || recipe.TmMappings.Count > 0;
            gameResponse = _areaDiscoveries.RestoreArchivedDetails(runId, gameResponse, itemReconstructionAvailable);
        }

        AreaLookupDetailResponsePayload response = WithTrackerRevision(runId, gameResponse);
        _areaDetailCache[$"{source}|{runId}|{response.Revision}|{diagnosticAccess}|{category}|{areaId}"] = response;
        return response;
    }

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
    public async Task<PokemonSearchResponsePayload> SearchPokemonAsync(CompletedRunRecipePayload recipe, string query, int offset = 0, int limit = TrackerProtocol.DefaultSearchPageSize, bool normalOnly = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, TrackerProtocol.MinimumSearchPageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, TrackerProtocol.MaximumSearchPageSize);
        string normalizedQuery = query.Trim();
        string cacheKey = $"{recipe.RunId}|{normalOnly}|{offset}|{limit}|{normalizedQuery.ToUpperInvariant()}";
        if (_pokemonSearchCache.TryGetValue(cacheKey, out PokemonSearchResponsePayload? cached))
            return cached;

        PokemonSearchRequestPayload payload = new() { Query = normalizedQuery, Offset = offset, Limit = limit, NormalOnly = normalOnly, Recipe = recipe };
        PokemonSearchResponsePayload response = await _session.SendAsync<PokemonSearchRequestPayload, PokemonSearchResponsePayload>(TrackerCommands.PokemonSearch, payload, recipe.RunId, cancellationToken);
        _pokemonSearchCache[cacheKey] = response;
        return response;
    }

    /// <summary>
    /// Requests complete deterministic information for one Pokémon in a completed run.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The selected stable species and form identifier.</param>
    /// <param name="section">The independently requested information section.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The reconstructed Pokémon information.</returns>
    public async Task<PokemonLookupSnapshot> LookupPokemonAsync(CompletedRunRecipePayload recipe, string speciesId, PokemonLookupSection section = PokemonLookupSection.Overview, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        string cacheKey = $"{recipe.RunId}|{speciesId.ToUpperInvariant()}|{section}";
        if (_pokemonLookupCache.TryGetValue(cacheKey, out PokemonLookupSnapshot? cached))
            return cached;

        PokemonLookupRequestPayload payload = new() { SpeciesId = speciesId, Level = TrackerProtocol.CompatibilityLookupLevel, Section = section, Recipe = recipe };
        PokemonLookupSnapshot response = await _session.SendAsync<PokemonLookupRequestPayload, PokemonLookupSnapshot>(TrackerCommands.PokemonLookup, payload, recipe.RunId, cancellationToken);
        _pokemonLookupCache[cacheKey] = response;
        return response;
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
    public async Task<EvolutionCandidateSearchResponsePayload> SearchEvolutionCandidatesAsync(CompletedRunRecipePayload recipe, string speciesId, EvolutionCandidateSide side, string query, int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        string normalizedQuery = query.Trim();
        string cacheKey = $"{recipe.RunId}|{speciesId.ToUpperInvariant()}|{side}|{offset}|{normalizedQuery.ToUpperInvariant()}";
        if (_evolutionCandidateCache.TryGetValue(cacheKey, out EvolutionCandidateSearchResponsePayload? cached))
            return cached;

        EvolutionCandidateSearchRequestPayload request = new() { SpeciesId = speciesId, Side = side, Query = normalizedQuery, Offset = offset, Recipe = recipe };
        EvolutionCandidateSearchResponsePayload response = await _session.SendAsync<EvolutionCandidateSearchRequestPayload, EvolutionCandidateSearchResponsePayload>(TrackerCommands.EvolutionCandidateSearch, request, recipe.RunId, cancellationToken);
        _evolutionCandidateCache[cacheKey] = response;
        return response;
    }

    /// <summary>
    /// Requests one page of normal material pairs which produce a completed-run fusion.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The fusion species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested material-pair page.</returns>
    public async Task<FusionMaterialSearchResponsePayload> SearchFusionMaterialsAsync(CompletedRunRecipePayload recipe, string speciesId, int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        string cacheKey = $"{recipe.RunId}|{speciesId.ToUpperInvariant()}|{offset}";
        if (_fusionMaterialCache.TryGetValue(cacheKey, out FusionMaterialSearchResponsePayload? cached))
            return cached;

        FusionMaterialSearchRequestPayload request = new() { SpeciesId = speciesId, Offset = offset, Recipe = recipe };
        FusionMaterialSearchResponsePayload response = await _session.SendAsync<FusionMaterialSearchRequestPayload, FusionMaterialSearchResponsePayload>(TrackerCommands.FusionMaterialSearch, request, recipe.RunId, cancellationToken);
        _fusionMaterialCache[cacheKey] = response;
        return response;
    }

    /// <summary>
    /// Requests one page of wild occurrences for a completed-run Pokemon.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The generated species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested wild-occurrence page.</returns>
    public async Task<WildOccurrenceSearchResponsePayload> SearchWildOccurrencesAsync(CompletedRunRecipePayload recipe, string speciesId, int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        string cacheKey = $"{recipe.RunId}|{speciesId.ToUpperInvariant()}|{offset}";
        if (_wildOccurrenceCache.TryGetValue(cacheKey, out WildOccurrenceSearchResponsePayload? cached))
            return cached;

        WildOccurrenceSearchRequestPayload request = new() { SpeciesId = speciesId, Offset = offset, Recipe = recipe };
        WildOccurrenceSearchResponsePayload response = await _session.SendAsync<WildOccurrenceSearchRequestPayload, WildOccurrenceSearchResponsePayload>(TrackerCommands.WildOccurrenceSearch, request, recipe.RunId, cancellationToken);
        _wildOccurrenceCache[cacheKey] = response;
        return response;
    }

    /// <summary>
    /// Requests one page of trainer occurrences for a completed-run Pokemon.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The generated species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested trainer-occurrence page.</returns>
    public async Task<TrainerOccurrenceSearchResponsePayload> SearchTrainerOccurrencesAsync(CompletedRunRecipePayload recipe, string speciesId, int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        string cacheKey = $"{recipe.RunId}|{speciesId.ToUpperInvariant()}|{offset}";
        if (_trainerOccurrenceCache.TryGetValue(cacheKey, out TrainerOccurrenceSearchResponsePayload? cached))
            return cached;

        TrainerOccurrenceSearchRequestPayload request = new() { SpeciesId = speciesId, Offset = offset, Recipe = recipe };
        TrainerOccurrenceSearchResponsePayload response = await _session.SendAsync<TrainerOccurrenceSearchRequestPayload, TrainerOccurrenceSearchResponsePayload>(TrackerCommands.TrainerOccurrenceSearch, request, recipe.RunId, cancellationToken);
        _trainerOccurrenceCache[cacheKey] = response;
        return response;
    }

    /// <summary>
    /// Requests both deterministic Ironmon fusion orientations for two normal Pokémon.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="firstSpeciesId">The first normal species identifier.</param>
    /// <param name="secondSpeciesId">The second normal species identifier.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The distinct deterministic fusion outcomes.</returns>
    public async Task<FusionPreviewResponsePayload> PreviewFusionAsync(CompletedRunRecipePayload recipe, string firstSpeciesId, string secondSpeciesId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstSpeciesId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secondSpeciesId);
        string cacheKey = $"{recipe.RunId}|{firstSpeciesId.ToUpperInvariant()}|{secondSpeciesId.ToUpperInvariant()}";
        if (_fusionPreviewCache.TryGetValue(cacheKey, out FusionPreviewResponsePayload? cached))
            return cached;

        FusionPreviewRequestPayload payload = new() { FirstSpeciesId = firstSpeciesId, SecondSpeciesId = secondSpeciesId, Recipe = recipe };
        FusionPreviewResponsePayload response = await _session.SendAsync<FusionPreviewRequestPayload, FusionPreviewResponsePayload>(TrackerCommands.FusionPreview, payload, recipe.RunId, cancellationToken);
        _fusionPreviewCache[cacheKey] = response;
        return response;
    }

    /// <summary>
    /// Requests the game-owned Ironmon inspector data for one current Pokémon.
    /// </summary>
    /// <param name="request">The Pokémon source selection.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The complete authorized inspector snapshot.</returns>
    public Task<DebugPokemonInspectorSnapshot> InspectPokemonAsync(DebugPokemonInspectionRequestPayload request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureDiagnosticCapabilities(GetAvailabilityCapability(request.Target));
        EnsurePokemonInformationCapability(request.Section);
        return _session.SendAsync<DebugPokemonInspectionRequestPayload, DebugPokemonInspectorSnapshot>(TrackerCommands.DebugInspectPokemon, request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Requests game-owned run and randomizer diagnostics.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The authorized run diagnostics.</returns>
    public Task<DebugRunDiagnosticsSnapshot> GetDebugRunDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        EnsureAnyDiagnosticCapability(DiagnosticCapabilities.RunConfiguration, DiagnosticCapabilities.RunSeed, DiagnosticCapabilities.RunGeneratorManifests, DiagnosticCapabilities.EvolutionGeneratorDetails);
        DebugRunDiagnosticsRequestPayload request = new();
        return _session.SendAsync<DebugRunDiagnosticsRequestPayload, DebugRunDiagnosticsSnapshot>(TrackerCommands.DebugRunDiagnostics, request, GetConnectedRunId(), cancellationToken);
    }

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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, TrackerProtocol.MinimumSearchPageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, TrackerProtocol.MaximumSearchPageSize);
        EnsureDiagnosticCapabilities(DiagnosticCapabilities.PokemonAllActive);
        DebugPokemonSearchRequestPayload request = new() { Query = query.Trim(), Offset = offset, Limit = limit, NormalOnly = normalOnly };
        return _session.SendAsync<DebugPokemonSearchRequestPayload, PokemonSearchResponsePayload>(TrackerCommands.DebugPokemonSearch, request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Requests generated information for one Pokémon in the active debug run.
    /// </summary>
    /// <param name="speciesId">The selected stable species and form identifier.</param>
    /// <param name="section">The independently requested information section.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The active-run generated Pokémon information.</returns>
    public Task<PokemonLookupSnapshot> LookupDebugPokemonAsync(string speciesId, PokemonLookupSection section = PokemonLookupSection.Overview, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        EnsureDiagnosticCapabilities(DiagnosticCapabilities.PokemonAllActive, GetInformationCapability(section));
        DebugPokemonLookupRequestPayload request = new() { SpeciesId = speciesId, Section = section };
        return _session.SendAsync<DebugPokemonLookupRequestPayload, PokemonLookupSnapshot>(TrackerCommands.DebugPokemonLookup, request, GetConnectedRunId(), cancellationToken);
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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        EnsureDebugPokemonSource(target);
        EnsureDiagnosticCapabilities(DiagnosticCapabilities.EvolutionCandidates);
        DebugEvolutionCandidateSearchRequestPayload request = new() { SpeciesId = speciesId, Target = target, EnemyPosition = enemyPosition, Side = side, Query = query.Trim(), Offset = offset };
        return _session.SendAsync<DebugEvolutionCandidateSearchRequestPayload, EvolutionCandidateSearchResponsePayload>(TrackerCommands.DebugEvolutionCandidateSearch, request, GetConnectedRunId(), cancellationToken);
    }

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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        EnsureDebugPokemonSource(target);
        EnsureDiagnosticCapabilities(DiagnosticCapabilities.FusionMaterialPairs);
        DebugFusionMaterialSearchRequestPayload request = new() { SpeciesId = speciesId, Target = target, EnemyPosition = enemyPosition, Offset = offset };
        return _session.SendAsync<DebugFusionMaterialSearchRequestPayload, FusionMaterialSearchResponsePayload>(TrackerCommands.DebugFusionMaterialSearch, request, GetConnectedRunId(), cancellationToken);
    }

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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        EnsureDebugPokemonSource(target);
        EnsureDiagnosticCapabilities(DiagnosticCapabilities.WorldWildEncounters);
        DebugWildOccurrenceSearchRequestPayload request = new() { SpeciesId = speciesId, Target = target, EnemyPosition = enemyPosition, Offset = offset };
        return _session.SendAsync<DebugWildOccurrenceSearchRequestPayload, WildOccurrenceSearchResponsePayload>(TrackerCommands.DebugWildOccurrenceSearch, request, GetConnectedRunId(), cancellationToken);
    }

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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        EnsureDebugPokemonSource(target);
        EnsureDiagnosticCapabilities(DiagnosticCapabilities.WorldTrainerParties);
        DebugTrainerOccurrenceSearchRequestPayload request = new() { SpeciesId = speciesId, Target = target, EnemyPosition = enemyPosition, Offset = offset };
        return _session.SendAsync<DebugTrainerOccurrenceSearchRequestPayload, TrainerOccurrenceSearchResponsePayload>(TrackerCommands.DebugTrainerOccurrenceSearch, request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Requests both generated fusion orientations for the active debug run.
    /// </summary>
    /// <param name="firstSpeciesId">The first normal fusion material.</param>
    /// <param name="secondSpeciesId">The second normal fusion material.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The active-run fusion outcomes.</returns>
    public Task<FusionPreviewResponsePayload> PreviewDebugFusionAsync(string firstSpeciesId, string secondSpeciesId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstSpeciesId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secondSpeciesId);
        EnsureDiagnosticCapabilities(DiagnosticCapabilities.FusionPreviewResults);
        DebugFusionPreviewRequestPayload request = new() { FirstSpeciesId = firstSpeciesId, SecondSpeciesId = secondSpeciesId };
        return _session.SendAsync<DebugFusionPreviewRequestPayload, FusionPreviewResponsePayload>(TrackerCommands.DebugFusionPreview, request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Clears connection-scoped caches and disconnects the underlying request session.
    /// </summary>
    internal void Disconnect()
    {
        _session.Disconnect();
        _areaDetailCache.Clear();
        _areaSummaryCache.Clear();
        _fusionPreviewCache.Clear();
        _fusionMaterialCache.Clear();
        _pokemonLookupCache.Clear();
        _pokemonSearchCache.Clear();
        _evolutionCandidateCache.Clear();
        _trainerOccurrenceCache.Clear();
        _wildOccurrenceCache.Clear();
    }

    /// <summary>
    /// Rejects diagnostic requests unless every required named capability is effective.
    /// </summary>
    /// <param name="capabilities">The capabilities required by the request.</param>
    /// <exception cref="InvalidOperationException">Thrown when diagnostic access is not authorized.</exception>
    private void EnsureDiagnosticCapabilities(params string[] capabilities)
    {
        if (capabilities.Any(capability => !HasDiagnosticCapability(capability)))
            throw new InvalidOperationException("The connected game has not been granted every diagnostic capability required by this request.");
    }

    /// <summary>
    /// Rejects a composite diagnostic request unless at least one supported section is effective.
    /// </summary>
    /// <param name="capabilities">The independently optional diagnostic sections.</param>
    /// <exception cref="InvalidOperationException">Thrown when no requested diagnostic section is authorized.</exception>
    private void EnsureAnyDiagnosticCapability(params string[] capabilities)
    {
        if (!capabilities.Any(HasDiagnosticCapability))
            throw new InvalidOperationException("The connected game has not been granted access to any run diagnostic section.");
    }

    /// <summary>
    /// Rejects an arbitrary species request without all-active access, or validates the selected live source.
    /// </summary>
    /// <param name="target">The optional live source that supplies the requested species.</param>
    private void EnsureDebugPokemonSource(DebugPokemonTarget? target)
    {
        if (target is null)
        {
            EnsureDiagnosticCapabilities(DiagnosticCapabilities.PokemonAllActive);
            return;
        }

        EnsureDiagnosticCapabilities(GetAvailabilityCapability(target.Value));
    }

    /// <summary>
    /// Rejects an inspector section unless at least one independently rendered surface is authorized.
    /// </summary>
    /// <param name="section">The requested shared information section.</param>
    private void EnsurePokemonInformationCapability(PokemonLookupSection section)
    {
        if (section == PokemonLookupSection.Overview)
        {
            EnsureAnyDiagnosticCapability(DiagnosticCapabilities.PokemonOverview, DiagnosticCapabilities.WorldWildEncounters, DiagnosticCapabilities.WorldTrainerParties, DiagnosticCapabilities.FusionMaterialPairs, DiagnosticCapabilities.FusionPreviewResults);
            return;
        }

        if (section == PokemonLookupSection.Evolutions)
        {
            EnsureAnyDiagnosticCapability(DiagnosticCapabilities.EvolutionResults, DiagnosticCapabilities.EvolutionCandidates);
            return;
        }

        EnsureDiagnosticCapabilities(GetInformationCapability(section));
    }

    /// <summary>
    /// Gets the availability capability required by one live Pokemon target.
    /// </summary>
    /// <param name="target">The requested live target.</param>
    /// <returns>The stable availability capability.</returns>
    private static string GetAvailabilityCapability(DebugPokemonTarget target)
    {
        return target switch
        {
            DebugPokemonTarget.Player => DiagnosticCapabilities.PokemonCurrentPlayer,
            DebugPokemonTarget.Enemy => DiagnosticCapabilities.PokemonCurrentEnemies,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Only the represented current player or current enemy may be inspected.")
        };
    }

    /// <summary>
    /// Gets the information capability required by one Pokemon section.
    /// </summary>
    /// <param name="section">The requested information section.</param>
    /// <returns>The stable information capability.</returns>
    private static string GetInformationCapability(PokemonLookupSection section)
    {
        return section switch
        {
            PokemonLookupSection.Overview => DiagnosticCapabilities.PokemonOverview,
            PokemonLookupSection.Abilities => DiagnosticCapabilities.PokemonAbilities,
            PokemonLookupSection.Stats => DiagnosticCapabilities.PokemonBaseStats,
            PokemonLookupSection.Moves => DiagnosticCapabilities.PokemonMoveAccess,
            PokemonLookupSection.Evolutions => DiagnosticCapabilities.EvolutionResults,
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, "The Pokemon lookup section is unsupported.")
        };
    }

    /// <summary>
    /// Gets the active-run information capability associated with one area category.
    /// </summary>
    /// <param name="category">The area content category.</param>
    /// <returns>The stable information capability.</returns>
    private static string GetAreaCapability(AreaContentCategory category)
    {
        return category switch
        {
            AreaContentCategory.Trainer => DiagnosticCapabilities.WorldTrainerParties,
            AreaContentCategory.Encounter => DiagnosticCapabilities.WorldWildEncounters,
            AreaContentCategory.Item => DiagnosticCapabilities.WorldItems,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "The area content category is unsupported.")
        };
    }

    /// <summary>
    /// Gets the active run identifier when reconnect recovery has provided one.
    /// </summary>
    /// <returns>The connected run identifier when available.</returns>
    private string? GetConnectedRunId()
        => _state.Snapshot.CurrentState?.RunId ?? _state.Snapshot.Game?.RunId;

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
            TrainerDefeated = category == AreaContentCategory.Trainer ? Math.Min(area.TrainerTotal, Math.Max(area.TrainerDefeated, _areaDiscoveries.GetCount(runId, area.AreaId, category))) : 0,
            EncounterTotal = area.EncounterTotal,
            Encountered = category == AreaContentCategory.Encounter ? Math.Min(area.EncounterTotal, _areaDiscoveries.GetCount(runId, area.AreaId, category)) : 0,
            ItemTotal = area.ItemTotal,
            ItemsCollected = category == AreaContentCategory.Item ? Math.Min(area.ItemTotal, Math.Max(area.ItemsCollected, _areaDiscoveries.GetCount(runId, area.AreaId, category))) : 0
        })];

        return new AreaLookupSummaryResponsePayload { SchemaVersion = response.SchemaVersion, Revision = _areaDiscoveries.GetRevision(runId), Areas = areas };
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
            Revision = _areaDiscoveries.GetRevision(runId),
            Trainers = response.Trainers,
            Encounters = response.Encounters,
            Items = response.Items
        };
    }
}
