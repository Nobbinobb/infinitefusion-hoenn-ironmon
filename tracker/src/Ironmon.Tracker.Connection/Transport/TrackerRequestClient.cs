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
    internal TrackerRequestClient(TrackerRequestSession session, TrackerConnectionOptions options, TrackerConnectionState state, AreaDiscoveryStore areaDiscoveries)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(areaDiscoveries);
        _session = session;
        _options = options;
        _state = state;
        _areaDiscoveries = areaDiscoveries;
    }

    /// <summary>
    /// Gets whether both the tracker launch mode and connected game authorize debug access.
    /// </summary>
    public bool DebugAuthorized => _options.DebugRequested && _state.Snapshot.Game?.DebugAvailable == true;

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
        string cacheKey = $"{source}|{runId}|{revision}|{DebugAuthorized}|{category}|{areaId}";
        if (!forceRefresh && _areaDetailCache.TryGetValue(cacheKey, out AreaLookupDetailResponsePayload? cached))
            return cached;

        AreaLookupDetailRequestPayload request = new() { AreaId = areaId, Category = category, Recipe = recipe, DiscoveryKeys = _areaDiscoveries.GetKeys(runId, areaId, category) };
        AreaLookupDetailResponsePayload gameResponse = await _session.SendAsync<AreaLookupDetailRequestPayload, AreaLookupDetailResponsePayload>(TrackerCommands.AreaLookupDetail, request, runId, cancellationToken);
        _areaDiscoveries.RecordDetails(runId, gameResponse);
        AreaLookupDetailResponsePayload response = WithTrackerRevision(runId, gameResponse);
        _areaDetailCache[$"{source}|{runId}|{response.Revision}|{DebugAuthorized}|{category}|{areaId}"] = response;
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
        EnsureDebugAuthorized();
        return _session.SendAsync<DebugPokemonInspectionRequestPayload, DebugPokemonInspectorSnapshot>(TrackerCommands.DebugInspectPokemon, request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Requests game-owned run and randomizer diagnostics.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The authorized run diagnostics.</returns>
    public Task<DebugRunDiagnosticsSnapshot> GetDebugRunDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        EnsureDebugAuthorized();
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
        EnsureDebugAuthorized();
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
        EnsureDebugAuthorized();
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
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested candidate page.</returns>
    public Task<EvolutionCandidateSearchResponsePayload> SearchDebugEvolutionCandidatesAsync(string speciesId, EvolutionCandidateSide side, string query, int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        EnsureDebugAuthorized();
        DebugEvolutionCandidateSearchRequestPayload request = new() { SpeciesId = speciesId, Side = side, Query = query.Trim(), Offset = offset };
        return _session.SendAsync<DebugEvolutionCandidateSearchRequestPayload, EvolutionCandidateSearchResponsePayload>(TrackerCommands.DebugEvolutionCandidateSearch, request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Requests one page of fusion-material pairs from the authorized active run.
    /// </summary>
    /// <param name="speciesId">The fusion species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested material-pair page.</returns>
    public Task<FusionMaterialSearchResponsePayload> SearchDebugFusionMaterialsAsync(string speciesId, int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        EnsureDebugAuthorized();
        DebugFusionMaterialSearchRequestPayload request = new() { SpeciesId = speciesId, Offset = offset };
        return _session.SendAsync<DebugFusionMaterialSearchRequestPayload, FusionMaterialSearchResponsePayload>(TrackerCommands.DebugFusionMaterialSearch, request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Requests one page of wild occurrences from the authorized active run.
    /// </summary>
    /// <param name="speciesId">The generated species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested wild-occurrence page.</returns>
    public Task<WildOccurrenceSearchResponsePayload> SearchDebugWildOccurrencesAsync(string speciesId, int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        EnsureDebugAuthorized();
        DebugWildOccurrenceSearchRequestPayload request = new() { SpeciesId = speciesId, Offset = offset };
        return _session.SendAsync<DebugWildOccurrenceSearchRequestPayload, WildOccurrenceSearchResponsePayload>(TrackerCommands.DebugWildOccurrenceSearch, request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Requests one page of trainer occurrences from the authorized active run.
    /// </summary>
    /// <param name="speciesId">The generated species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested trainer-occurrence page.</returns>
    public Task<TrainerOccurrenceSearchResponsePayload> SearchDebugTrainerOccurrencesAsync(string speciesId, int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        EnsureDebugAuthorized();
        DebugTrainerOccurrenceSearchRequestPayload request = new() { SpeciesId = speciesId, Offset = offset };
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
        EnsureDebugAuthorized();
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
    /// Rejects debug requests unless both sides of the handshake authorized access.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when debug access is not authorized.</exception>
    private void EnsureDebugAuthorized()
    {
        if (!DebugAuthorized)
            throw new InvalidOperationException("Both the tracker launch mode and connected game must authorize debug access.");
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
