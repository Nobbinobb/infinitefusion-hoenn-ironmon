using System.Text.Json;

namespace Ironmon.Tracker.Connection.Settings;

/// <summary>
/// Persists the user's unlimited list of normal favorite Pokemon.
/// </summary>
public sealed class FavoritePokemonStore
{
    private readonly string _path;
    private readonly Lock _sync = new();
    private readonly List<PokemonSearchMatch> _favorites;

    /// <summary>
    /// Initializes and loads the favorite-Pokemon store.
    /// </summary>
    /// <param name="options">The tracker-owned persistence location.</param>
    public FavoritePokemonStore(TrackerKnowledgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _path = Path.Combine(options.RootDirectory, TrackerStorageNames.SettingsDirectory, TrackerStorageNames.FavoritePokemonFile);
        _favorites = Load();
    }

    /// <summary>
    /// Gets the favorites in alphabetical display order.
    /// </summary>
    public IReadOnlyList<PokemonSearchMatch> Favorites
    {
        get
        {
            lock (_sync)
                return [.. _favorites];
        }
    }

    /// <summary>
    /// Gets the most recent non-fatal persistence error.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Adds a normal Pokemon unless it is already a favorite.
    /// </summary>
    /// <param name="favorite">The stable species identity and display name.</param>
    /// <returns><see langword="true"/> when the list changed.</returns>
    public bool Add(PokemonSearchMatch favorite)
    {
        ArgumentNullException.ThrowIfNull(favorite);
        lock (_sync)
        {
            if (_favorites.Any(candidate => candidate.SpeciesId.Equals(favorite.SpeciesId, StringComparison.OrdinalIgnoreCase)))
                return false;

            _favorites.Add(favorite);
            _favorites.Sort(static (left, right) => string.Compare(left.SpeciesName, right.SpeciesName, StringComparison.CurrentCultureIgnoreCase));
            return Save();
        }
    }

    /// <summary>
    /// Removes one favorite by its stable species identity.
    /// </summary>
    /// <param name="speciesId">The stable species and form identifier.</param>
    /// <returns><see langword="true"/> when the list changed.</returns>
    public bool Remove(string speciesId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        lock (_sync)
        {
            int removed = _favorites.RemoveAll(candidate => candidate.SpeciesId.Equals(speciesId, StringComparison.OrdinalIgnoreCase));
            return removed > 0 && Save();
        }
    }

    /// <summary>
    /// Loads valid favorites without making a damaged settings file fatal.
    /// </summary>
    /// <returns>The loaded favorites.</returns>
    private List<PokemonSearchMatch> Load()
    {
        if (!File.Exists(_path))
            return [];

        try
        {
            List<PokemonSearchMatch>? favorites = JsonSerializer.Deserialize<List<PokemonSearchMatch>>(File.ReadAllText(_path), TrackerJson.Options);
            LastError = null;
            return favorites?.Where(IsValid).DistinctBy(favorite => favorite.SpeciesId, StringComparer.OrdinalIgnoreCase).OrderBy(favorite => favorite.SpeciesName, StringComparer.CurrentCultureIgnoreCase).ToList() ?? [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            LastError = exception.Message;
            return [];
        }
    }

    /// <summary>
    /// Persists the complete list atomically.
    /// </summary>
    /// <returns><see langword="true"/> when persistence succeeded.</returns>
    private bool Save()
    {
        try
        {
            TrackerAtomicFileWriter.WriteAllText(_path, JsonSerializer.Serialize(_favorites, TrackerJson.Options));
            LastError = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LastError = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// Determines whether one deserialized favorite has its required identity fields.
    /// </summary>
    /// <param name="favorite">The candidate favorite.</param>
    /// <returns><see langword="true"/> when it can be retained.</returns>
    private static bool IsValid(PokemonSearchMatch favorite)
        => !string.IsNullOrWhiteSpace(favorite.SpeciesId) && !string.IsNullOrWhiteSpace(favorite.SpeciesName);
}
