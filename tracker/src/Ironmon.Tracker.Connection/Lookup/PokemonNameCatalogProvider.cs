namespace Ironmon.Tracker.Connection.Lookup;

/// <summary>
/// Shares already-prepared immutable fusion-name catalogs between connection request clients.
/// </summary>
/// <remarks>
/// Retains up to four generation profiles. Reading a catalog performs no game request, and
/// callers must confirm its fusion-name context token before assembling current lookup results.
/// </remarks>
internal sealed class PokemonNameCatalogProvider
{
    private const int Capacity = 4;
    private readonly Dictionary<string, PokemonNameCatalog> _catalogs = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();
    private string? _latestProfile;

    /// <summary>
    /// Publishes a copied fusion-name catalog after an authorized search import succeeds.
    /// </summary>
    /// <param name="payload">The successfully imported game-owned catalog.</param>
    /// <exception cref="ArgumentNullException">Thrown when the payload is null.</exception>
    /// <exception cref="InvalidDataException">Thrown when the catalog schema or component dimensions are unsupported.</exception>
    internal void Publish(PokemonNameCatalogPayload payload)
    {
        PokemonNameCatalog catalog = new(payload);
        lock (_sync)
        {
            if (_catalogs.Count >= Capacity && !_catalogs.ContainsKey(catalog.GenerationProfileId))
                _catalogs.Remove(_catalogs.Keys.First());

            _catalogs[catalog.GenerationProfileId] = catalog;
            _latestProfile = catalog.GenerationProfileId;
        }
    }

    /// <summary>
    /// Reads a retained catalog for a requested profile or the most recently selected profile.
    /// </summary>
    /// <param name="generationProfileId">The explicit profile, or null to use the last published or confirmed profile.</param>
    /// <param name="catalog">The immutable catalog on success, or null when no retained catalog is available.</param>
    /// <returns>Whether a catalog is retained for the selected profile.</returns>
    internal bool TryGet(string? generationProfileId, out PokemonNameCatalog catalog)
    {
        lock (_sync)
        {
            string? profile = generationProfileId ?? _latestProfile;
            if (profile is not null && _catalogs.TryGetValue(profile, out catalog!))
                return true;

            catalog = null!;
            return false;
        }
    }

    /// <summary>
    /// Selects a retained profile as the latest profile after its search is accepted by the game.
    /// </summary>
    /// <param name="generationProfileId">The profile confirmed by the bounded game search response.</param>
    internal void MarkUsed(string generationProfileId)
    {
        lock (_sync)
        {
            if (_catalogs.ContainsKey(generationProfileId))
                _latestProfile = generationProfileId;
        }
    }

    /// <summary>
    /// Removes all retained catalogs and the latest-profile selection when the owning peer disconnects.
    /// </summary>
    internal void Clear()
    {
        lock (_sync)
        {
            _catalogs.Clear();
            _latestProfile = null;
        }
    }
}
