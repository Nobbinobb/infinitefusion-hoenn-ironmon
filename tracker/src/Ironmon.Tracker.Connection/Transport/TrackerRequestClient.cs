using System.Collections.Concurrent;

namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Provides validated and cached game requests over the active tracker session.
/// </summary>
public sealed class TrackerRequestClient
{
    private readonly ConcurrentDictionary<string, FusionPreviewResponsePayload> _fusionPreviewCache = new();
    private readonly ConcurrentDictionary<string, PokemonLookupSnapshot> _pokemonLookupCache = new();
    private readonly ConcurrentDictionary<string, PokemonSearchResponsePayload> _pokemonSearchCache = new();
    private readonly TrackerConnectionOptions _options;
    private readonly TrackerRequestSession _session;
    private readonly TrackerConnectionState _state;

    /// <summary>
    /// Initializes a request client for one connection service.
    /// </summary>
    /// <param name="session">The correlated request session.</param>
    /// <param name="options">The tracker connection options.</param>
    /// <param name="state">The shared connection state.</param>
    internal TrackerRequestClient(TrackerRequestSession session, TrackerConnectionOptions options, TrackerConnectionState state)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(state);
        _session = session;
        _options = options;
        _state = state;
    }

    /// <summary>
    /// Gets whether both the tracker launch mode and connected game authorize debug access.
    /// </summary>
    public bool DebugAuthorized => _options.DebugRequested && _state.Snapshot.Game?.DebugAvailable == true;

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
    public async Task<PokemonSearchResponsePayload> SearchPokemonAsync(CompletedRunRecipePayload recipe, string query, int offset = 0, int limit = 20, bool normalOnly = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 50);
        string normalizedQuery = query.Trim();
        string cacheKey = $"{recipe.RunId}|{normalOnly}|{offset}|{limit}|{normalizedQuery.ToUpperInvariant()}";
        if (_pokemonSearchCache.TryGetValue(cacheKey, out PokemonSearchResponsePayload? cached))
            return cached;

        PokemonSearchRequestPayload payload = new() { Query = normalizedQuery, Offset = offset, Limit = limit, NormalOnly = normalOnly, Recipe = recipe };
        PokemonSearchResponsePayload response = await _session.SendAsync<PokemonSearchRequestPayload, PokemonSearchResponsePayload>("pokemon_search", payload, recipe.RunId, cancellationToken);
        _pokemonSearchCache[cacheKey] = response;
        return response;
    }

    /// <summary>
    /// Requests complete deterministic information for one Pokémon in a completed run.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The selected stable species and form identifier.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The reconstructed Pokémon information.</returns>
    public async Task<PokemonLookupSnapshot> LookupPokemonAsync(CompletedRunRecipePayload recipe, string speciesId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        string cacheKey = $"{recipe.RunId}|{speciesId.ToUpperInvariant()}";
        if (_pokemonLookupCache.TryGetValue(cacheKey, out PokemonLookupSnapshot? cached))
            return cached;

        PokemonLookupRequestPayload payload = new() { SpeciesId = speciesId, Level = 100, Recipe = recipe };
        PokemonLookupSnapshot response = await _session.SendAsync<PokemonLookupRequestPayload, PokemonLookupSnapshot>("pokemon_lookup", payload, recipe.RunId, cancellationToken);
        _pokemonLookupCache[cacheKey] = response;
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
        FusionPreviewResponsePayload response = await _session.SendAsync<FusionPreviewRequestPayload, FusionPreviewResponsePayload>("fusion_preview", payload, recipe.RunId, cancellationToken);
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
        return _session.SendAsync<DebugPokemonInspectionRequestPayload, DebugPokemonInspectorSnapshot>("debug_inspect_pokemon", request, GetConnectedRunId(), cancellationToken);
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
        return _session.SendAsync<DebugRunDiagnosticsRequestPayload, DebugRunDiagnosticsSnapshot>("debug_run_diagnostics", request, GetConnectedRunId(), cancellationToken);
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
    public Task<PokemonSearchResponsePayload> SearchDebugPokemonAsync(string query, int offset = 0, int limit = 20, bool normalOnly = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 50);
        EnsureDebugAuthorized();
        DebugPokemonSearchRequestPayload request = new() { Query = query.Trim(), Offset = offset, Limit = limit, NormalOnly = normalOnly };
        return _session.SendAsync<DebugPokemonSearchRequestPayload, PokemonSearchResponsePayload>("debug_pokemon_search", request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Requests generated information for one Pokémon in the active debug run.
    /// </summary>
    /// <param name="speciesId">The selected stable species and form identifier.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The active-run generated Pokémon information.</returns>
    public Task<PokemonLookupSnapshot> LookupDebugPokemonAsync(string speciesId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        EnsureDebugAuthorized();
        DebugPokemonLookupRequestPayload request = new() { SpeciesId = speciesId };
        return _session.SendAsync<DebugPokemonLookupRequestPayload, PokemonLookupSnapshot>("debug_pokemon_lookup", request, GetConnectedRunId(), cancellationToken);
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
        return _session.SendAsync<DebugFusionPreviewRequestPayload, FusionPreviewResponsePayload>("debug_fusion_preview", request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Clears connection-scoped caches and disconnects the underlying request session.
    /// </summary>
    internal void Disconnect()
    {
        _session.Disconnect();
        _fusionPreviewCache.Clear();
        _pokemonLookupCache.Clear();
        _pokemonSearchCache.Clear();
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
}
