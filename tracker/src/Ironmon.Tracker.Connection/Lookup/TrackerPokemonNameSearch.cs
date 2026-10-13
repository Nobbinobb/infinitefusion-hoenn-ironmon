using Ironmon.Tracker.Connection.Transport;

namespace Ironmon.Tracker.Connection.Lookup;

/// <summary>
/// Coordinates tracker name matching with authoritative game validation and result annotation.
/// </summary>
/// <remarks>
/// Compatible fusion searches use bounded connection-scoped indexes. Normal-only searches,
/// non-ASCII queries, and unsupported peers or local runtimes use the original game search path.
/// </remarks>
internal sealed class TrackerPokemonNameSearch
{
    private const string CatalogChanged = "name_catalog_changed";
    private const int CatalogCapacity = 4;
    private readonly TrackerRequestSession _session;
    private readonly TrackerConnectionState _state;
    private readonly PokemonNameCatalogProvider _readyNames;
    private readonly SemaphoreSlim _preparationGate = new(1, 1);
    private readonly Dictionary<string, PokemonSearchCatalog> _catalogs = new(StringComparer.Ordinal);
    private readonly Lock _catalogSync = new();
    private readonly Task<bool> _runtimePreparation;
    private int _generation;

    /// <summary>
    /// Initializes a connection-scoped search coordinator and starts supported local runtime preparation.
    /// </summary>
    /// <param name="session">The request session used for game authorization, validation, and annotation.</param>
    /// <param name="state">The connection state exposing the current peer's capabilities.</param>
    /// <param name="readyNames">The shared store receiving successfully prepared fusion-name catalogs.</param>
    internal TrackerPokemonNameSearch(TrackerRequestSession session, TrackerConnectionState state, PokemonNameCatalogProvider readyNames)
    {
        _session = session;
        _state = state;
        _readyNames = readyNames;
        _runtimePreparation = OperatingSystem.IsWindows() && Environment.Is64BitProcess
            ? Task.Run(PokemonNameSearchRuntime.Prepare)
            : Task.FromResult(false);
    }

    /// <summary>
    /// Searches through a compatible tracker index and obtains the game's validated, annotated page.
    /// </summary>
    /// <remarks>
    /// Unsupported search modes use the original game path. Tracker matching imports a catalog
    /// when needed, reuses ordered query pages, and retries once when the game rejects a stale
    /// catalog context. Cancellation applies to pending preparation, uncached matching, and requests.
    /// </remarks>
    /// <param name="command">The existing authorized search command.</param>
    /// <param name="recipe">The archived recipe, or null for active searches.</param>
    /// <param name="query">The trimmed query.</param>
    /// <param name="offset">The zero-based page offset.</param>
    /// <param name="limit">The bounded page size.</param>
    /// <param name="normalOnly">Whether only normal species are requested.</param>
    /// <param name="runId">The owning run identifier, or null for an unscoped request.</param>
    /// <param name="cancellationToken">The token that cancels waits, index construction, uncached matching, and game requests.</param>
    /// <returns>A task whose result is the authoritative game's validated and annotated search response.</returns>
    /// <exception cref="OperationCanceledException">Thrown when a cancellable wait, matching pass, or request observes cancellation.</exception>
    /// <exception cref="InvalidDataException">Thrown when the game supplies no requested catalog or supplies an unsupported catalog.</exception>
    /// <exception cref="IOException">Thrown when the peer disconnects before a prepared index can be published.</exception>
    /// <exception cref="TrackerProtocolException">Thrown when the game rejects the request, including a stale catalog after the retry.</exception>
    internal async Task<PokemonSearchResponsePayload> SearchAsync(string command, CompletedRunRecipePayload? recipe, string query, int offset, int limit, bool normalOnly, string? runId, CancellationToken cancellationToken)
    {
        if (normalOnly || !OperatingSystem.IsWindows() || query.Any(character => character > 127) || _state.Snapshot.Game?.PokemonNameCatalogSupported != true)
            return await SendAsync(command, recipe, query, offset, limit, normalOnly, runId, null, false, cancellationToken).ConfigureAwait(false);

        if (!await _runtimePreparation.WaitAsync(cancellationToken).ConfigureAwait(false))
            return await SendAsync(command, recipe, query, offset, limit, normalOnly, runId, null, false, cancellationToken).ConfigureAwait(false);

        string key = $"{command}|{runId}|{recipe?.GenerationProfileId}";
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await _preparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            PokemonSearchCatalog catalog;
            try
            {
                bool found;
                int generation;
                lock (_catalogSync)
                {
                    found = _catalogs.TryGetValue(key, out catalog!);
                    generation = _generation;
                }

                if (!found)
                {
                    PokemonSearchResponsePayload response = await SendAsync(command, recipe, query, offset, limit, false, runId, null, true, cancellationToken).ConfigureAwait(false);
                    PokemonNameCatalogPayload payload = response.NameCatalog ?? throw new InvalidDataException("The game did not supply the requested name catalog.");
                    catalog = await Task.Run(() => new PokemonSearchCatalog(payload, cancellationToken), cancellationToken).ConfigureAwait(false);
                    lock (_catalogSync)
                    {
                        if (_generation != generation)
                            throw new IOException("The game disconnected during Pokemon name preparation.");

                        if (_catalogs.Count >= CatalogCapacity)
                            _catalogs.Remove(_catalogs.Keys.First());
                        _catalogs[key] = catalog;
                        _readyNames.Publish(payload);
                    }
                }
            }
            finally
            {
                _preparationGate.Release();
            }

            PokemonNameSearchAssignmentPayload assignment = catalog.TryGetCachedPage(query, offset, limit, out PokemonNameSearchAssignmentPayload cachedPage)
                ? cachedPage
                : await Task.Run(() => catalog.Search(query, offset, limit, cancellationToken), cancellationToken).ConfigureAwait(false);
            try
            {
                PokemonSearchResponsePayload response = await SendAsync(command, recipe, query, offset, limit, false, runId, assignment, false, cancellationToken).ConfigureAwait(false);
                _readyNames.MarkUsed(catalog.GenerationProfileId);
                return response;
            }
            catch (TrackerProtocolException exception) when (exception.ErrorCode == CatalogChanged && attempt == 0)
            {
                lock (_catalogSync)
                {
                    if (_catalogs.TryGetValue(key, out PokemonSearchCatalog? current) && ReferenceEquals(current, catalog))
                        _catalogs.Remove(key);
                }
            }
        }

        throw new InvalidOperationException("The Pokemon name context changed repeatedly.");
    }

    /// <summary>
    /// Invalidates disconnected-peer catalogs and prevents older preparations from publishing an index.
    /// </summary>
    /// <remarks>
    /// Advances the preparation generation and clears both search indexes and shared ready names.
    /// Pending game requests remain under the request session's lifecycle control.
    /// </remarks>
    internal void Disconnect()
    {
        lock (_catalogSync)
        {
            _generation++;
            _catalogs.Clear();
            _readyNames.Clear();
        }
    }

    /// <summary>
    /// Sends an archived-recipe or active debug payload through the existing game search command.
    /// </summary>
    /// <param name="command">The search command.</param>
    /// <param name="recipe">The optional archived recipe.</param>
    /// <param name="query">The trimmed search query.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="limit">The maximum number of results to request.</param>
    /// <param name="normalOnly">Whether fusion searches are excluded.</param>
    /// <param name="runId">The owning run identifier, or null for an unscoped request.</param>
    /// <param name="assignment">The optional tracker-matched page requiring game context and species validation.</param>
    /// <param name="catalogOnly">Whether only compact name inputs are requested.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A task yielding the game's compact catalog or validated search page for the requested mode.</returns>
    private Task<PokemonSearchResponsePayload> SendAsync(string command, CompletedRunRecipePayload? recipe, string query, int offset, int limit, bool normalOnly, string? runId, PokemonNameSearchAssignmentPayload? assignment, bool catalogOnly, CancellationToken cancellationToken)
    {
        if (recipe is not null)
        {
            PokemonSearchRequestPayload payload = new() { Query = query, Offset = offset, Limit = limit, NormalOnly = normalOnly, Recipe = recipe, NameCatalogOnly = catalogOnly, NameSearchAssignment = assignment };
            return _session.SendAsync<PokemonSearchRequestPayload, PokemonSearchResponsePayload>(command, payload, runId, cancellationToken);
        }

        DebugPokemonSearchRequestPayload request = new() { Query = query, Offset = offset, Limit = limit, NormalOnly = normalOnly, NameCatalogOnly = catalogOnly, NameSearchAssignment = assignment };
        return _session.SendAsync<DebugPokemonSearchRequestPayload, PokemonSearchResponsePayload>(command, request, runId, cancellationToken);
    }
}
