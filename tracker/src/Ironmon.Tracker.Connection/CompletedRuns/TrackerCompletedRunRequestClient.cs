using Ironmon.Tracker.Connection.Transport;

namespace Ironmon.Tracker.Connection.CompletedRuns;

/// <summary>
/// Coordinates cached deterministic lookup requests for completed-run recipes.
/// </summary>
internal sealed class TrackerCompletedRunRequestClient
{
    private readonly TrackerResponseCache _cache;
    private readonly TrackerRequestSession _session;

    /// <summary>
    /// Initializes completed-run request orchestration for one tracker connection.
    /// </summary>
    /// <param name="session">The correlated request session.</param>
    /// <param name="cache">The connection-scoped response cache.</param>
    internal TrackerCompletedRunRequestClient(TrackerRequestSession session, TrackerResponseCache cache)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(cache);
        _session = session;
        _cache = cache;
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
    internal async Task<PokemonSearchResponsePayload> SearchPokemonAsync(CompletedRunRecipePayload recipe, string query, int offset, int limit, bool normalOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, TrackerProtocol.MinimumSearchPageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, TrackerProtocol.MaximumSearchPageSize);
        string normalizedQuery = query.Trim();
        string cacheKey = $"{recipe.RunId}|{normalOnly}|{offset}|{limit}|{normalizedQuery.ToUpperInvariant()}";
        if (_cache.TryGet(TrackerCommands.PokemonSearch, cacheKey, out PokemonSearchResponsePayload cached))
            return cached;

        PokemonSearchRequestPayload payload = new() { Query = normalizedQuery, Offset = offset, Limit = limit, NormalOnly = normalOnly, Recipe = recipe };
        PokemonSearchResponsePayload response = await _session.SendAsync<PokemonSearchRequestPayload, PokemonSearchResponsePayload>(TrackerCommands.PokemonSearch, payload, recipe.RunId, cancellationToken);
        _cache.Set(TrackerCommands.PokemonSearch, cacheKey, response);
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
    internal async Task<PokemonLookupSnapshot> LookupPokemonAsync(CompletedRunRecipePayload recipe, string speciesId, PokemonLookupSection section, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        string cacheKey = $"{recipe.RunId}|{speciesId.ToUpperInvariant()}|{section}";
        if (_cache.TryGet(TrackerCommands.PokemonLookup, cacheKey, out PokemonLookupSnapshot cached))
            return cached;

        PokemonLookupRequestPayload payload = new() { SpeciesId = speciesId, Level = TrackerProtocol.CompatibilityLookupLevel, Section = section, Recipe = recipe };
        PokemonLookupSnapshot response = await _session.SendAsync<PokemonLookupRequestPayload, PokemonLookupSnapshot>(TrackerCommands.PokemonLookup, payload, recipe.RunId, cancellationToken);
        _cache.Set(TrackerCommands.PokemonLookup, cacheKey, response);
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
    internal async Task<EvolutionCandidateSearchResponsePayload> SearchEvolutionCandidatesAsync(CompletedRunRecipePayload recipe, string speciesId, EvolutionCandidateSide side, string query, int offset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        string normalizedQuery = query.Trim();
        string cacheKey = $"{recipe.RunId}|{speciesId.ToUpperInvariant()}|{side}|{offset}|{normalizedQuery.ToUpperInvariant()}";
        if (_cache.TryGet(TrackerCommands.EvolutionCandidateSearch, cacheKey, out EvolutionCandidateSearchResponsePayload cached))
            return cached;

        EvolutionCandidateSearchRequestPayload request = new() { SpeciesId = speciesId, Side = side, Query = normalizedQuery, Offset = offset, Recipe = recipe };
        EvolutionCandidateSearchResponsePayload response = await _session.SendAsync<EvolutionCandidateSearchRequestPayload, EvolutionCandidateSearchResponsePayload>(TrackerCommands.EvolutionCandidateSearch, request, recipe.RunId, cancellationToken);
        _cache.Set(TrackerCommands.EvolutionCandidateSearch, cacheKey, response);
        return response;
    }

    /// <summary>
    /// Requests one progressive page of generated evolution predecessors for a completed run.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The target species identifier.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="limit">The maximum number of predecessors to return.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The requested predecessor page.</returns>
    internal async Task<EvolutionPredecessorSearchResponsePayload> SearchEvolutionPredecessorsAsync(CompletedRunRecipePayload recipe, string speciesId, int offset, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, TrackerProtocol.MinimumSearchPageSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, TrackerProtocol.MaximumSearchPageSize);
        string cacheKey = $"{recipe.RunId}|{speciesId.ToUpperInvariant()}|{offset}|{limit}";
        if (_cache.TryGet(TrackerCommands.EvolutionPredecessorSearch, cacheKey, out EvolutionPredecessorSearchResponsePayload cached))
            return cached;

        EvolutionPredecessorSearchRequestPayload request = new() { SpeciesId = speciesId, Offset = offset, Limit = limit, Recipe = recipe };
        EvolutionPredecessorSearchResponsePayload response = await _session.SendAsync<EvolutionPredecessorSearchRequestPayload, EvolutionPredecessorSearchResponsePayload>(TrackerCommands.EvolutionPredecessorSearch, request, recipe.RunId, cancellationToken);
        _cache.Set(TrackerCommands.EvolutionPredecessorSearch, cacheKey, response);
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
    internal async Task<FusionMaterialSearchResponsePayload> SearchFusionMaterialsAsync(CompletedRunRecipePayload recipe, string speciesId, int offset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        string cacheKey = $"{recipe.RunId}|{speciesId.ToUpperInvariant()}|{offset}";
        if (_cache.TryGet(TrackerCommands.FusionMaterialSearch, cacheKey, out FusionMaterialSearchResponsePayload cached))
            return cached;

        FusionMaterialSearchRequestPayload request = new() { SpeciesId = speciesId, Offset = offset, Recipe = recipe };
        FusionMaterialSearchResponsePayload response = await _session.SendAsync<FusionMaterialSearchRequestPayload, FusionMaterialSearchResponsePayload>(TrackerCommands.FusionMaterialSearch, request, recipe.RunId, cancellationToken);
        _cache.Set(TrackerCommands.FusionMaterialSearch, cacheKey, response);
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
    internal async Task<WildOccurrenceSearchResponsePayload> SearchWildOccurrencesAsync(CompletedRunRecipePayload recipe, string speciesId, int offset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        string cacheKey = $"{recipe.RunId}|{speciesId.ToUpperInvariant()}|{offset}";
        if (_cache.TryGet(TrackerCommands.WildOccurrenceSearch, cacheKey, out WildOccurrenceSearchResponsePayload cached))
            return cached;

        WildOccurrenceSearchRequestPayload request = new() { SpeciesId = speciesId, Offset = offset, Recipe = recipe };
        WildOccurrenceSearchResponsePayload response = await _session.SendAsync<WildOccurrenceSearchRequestPayload, WildOccurrenceSearchResponsePayload>(TrackerCommands.WildOccurrenceSearch, request, recipe.RunId, cancellationToken);
        _cache.Set(TrackerCommands.WildOccurrenceSearch, cacheKey, response);
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
    internal async Task<TrainerOccurrenceSearchResponsePayload> SearchTrainerOccurrencesAsync(CompletedRunRecipePayload recipe, string speciesId, int offset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        string cacheKey = $"{recipe.RunId}|{speciesId.ToUpperInvariant()}|{offset}";
        if (_cache.TryGet(TrackerCommands.TrainerOccurrenceSearch, cacheKey, out TrainerOccurrenceSearchResponsePayload cached))
            return cached;

        TrainerOccurrenceSearchRequestPayload request = new() { SpeciesId = speciesId, Offset = offset, Recipe = recipe };
        TrainerOccurrenceSearchResponsePayload response = await _session.SendAsync<TrainerOccurrenceSearchRequestPayload, TrainerOccurrenceSearchResponsePayload>(TrackerCommands.TrainerOccurrenceSearch, request, recipe.RunId, cancellationToken);
        _cache.Set(TrackerCommands.TrainerOccurrenceSearch, cacheKey, response);
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
    internal async Task<FusionPreviewResponsePayload> PreviewFusionAsync(CompletedRunRecipePayload recipe, string firstSpeciesId, string secondSpeciesId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstSpeciesId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secondSpeciesId);
        string cacheKey = $"{recipe.RunId}|{firstSpeciesId.ToUpperInvariant()}|{secondSpeciesId.ToUpperInvariant()}";
        if (_cache.TryGet(TrackerCommands.FusionPreview, cacheKey, out FusionPreviewResponsePayload cached))
            return cached;

        FusionPreviewRequestPayload payload = new() { FirstSpeciesId = firstSpeciesId, SecondSpeciesId = secondSpeciesId, Recipe = recipe };
        FusionPreviewResponsePayload response = await _session.SendAsync<FusionPreviewRequestPayload, FusionPreviewResponsePayload>(TrackerCommands.FusionPreview, payload, recipe.RunId, cancellationToken);
        _cache.Set(TrackerCommands.FusionPreview, cacheKey, response);
        return response;
    }
}
