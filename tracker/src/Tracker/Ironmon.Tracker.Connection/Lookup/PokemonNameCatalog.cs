using System.Text;

namespace Ironmon.Tracker.Connection.Lookup;

/// <summary>
/// Stores immutable game-supplied name parts and composes fusion display names.
/// </summary>
/// <remarks>
/// Each catalog belongs to one generation profile and fusion-name context. Callers confirm
/// its context token with the game before using names in a current lookup response.
/// </remarks>
internal sealed class PokemonNameCatalog
{
    private readonly string?[] _prefixes;
    private readonly string?[] _suffixes;
    private readonly string?[] _capitalizedSuffixes;

    /// <summary>
    /// Gets the generation profile supplied by the game for these name parts.
    /// </summary>
    internal string GenerationProfileId { get; }

    /// <summary>
    /// Gets the shared fusion-name context token that lookup requests confirm with the game.
    /// </summary>
    internal string FusionNameContextToken { get; }

    /// <summary>
    /// Gets the number of one-based normal species identifiers on each fusion-component axis.
    /// </summary>
    internal int NormalSpeciesCount { get; }

    /// <summary>
    /// Initializes a catalog by validating its component dimensions and copying the name-part arrays.
    /// </summary>
    /// <param name="payload">The catalog supplied in an authorized game response.</param>
    /// <exception cref="ArgumentNullException">Thrown when the payload is null.</exception>
    /// <exception cref="InvalidDataException">Thrown when the schema or component dimensions are unsupported.</exception>
    internal PokemonNameCatalog(PokemonNameCatalogPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.SchemaVersion != 1 || payload.NormalSpeciesCount < 1 || payload.Prefixes.Count != payload.NormalSpeciesCount + 1 || payload.Suffixes.Count != payload.NormalSpeciesCount + 1)
            throw new InvalidDataException("The compact Pokemon name catalog is malformed.");

        _prefixes = [.. payload.Prefixes];
        _suffixes = [.. payload.Suffixes];
        _capitalizedSuffixes = [.. payload.CapitalizedSuffixes];
        GenerationProfileId = payload.GenerationProfileId;
        FusionNameContextToken = payload.FusionNameContextToken ?? payload.ContextToken;
        NormalSpeciesCount = payload.NormalSpeciesCount;
    }

    /// <summary>
    /// Composes a fusion name using native boundary trimming and suffix capitalization rules.
    /// </summary>
    /// <remarks>
    /// Equal Unicode characters at the part boundary are collapsed. Game-supplied capitalization
    /// is used when available; otherwise suffix capitalization uses invariant Unicode casing.
    /// </remarks>
    /// <param name="bodyId">The one-based normal body identifier.</param>
    /// <param name="headId">The one-based normal head identifier.</param>
    /// <param name="name">The complete display name on success, or an empty string when a component is unavailable.</param>
    /// <returns>Whether both identifiers are in range and both required name parts are present.</returns>
    internal bool TryGetFusionName(int bodyId, int headId, out string name)
    {
        name = string.Empty;
        if (bodyId < 1 || bodyId >= _suffixes.Length || headId < 1 || headId >= _prefixes.Length || _prefixes[headId] is not string prefix || _suffixes[bodyId] is not string suffix)
            return false;

        if (prefix.Length > 0 && suffix.Length > 0 && Rune.GetRuneAt(prefix, prefix.Length - (char.IsLowSurrogate(prefix[^1]) ? 2 : 1)) == Rune.GetRuneAt(suffix, 0))
            prefix = prefix[..^(char.IsLowSurrogate(prefix[^1]) ? 2 : 1)];

        if (prefix.EndsWith(' ') && suffix.Length > 0)
        {
            if (_capitalizedSuffixes.Length == _suffixes.Length && _capitalizedSuffixes[bodyId] is string capitalized)
            {
                name = prefix + capitalized;
                return true;
            }

            Rune first = Rune.GetRuneAt(suffix, 0);
            suffix = Rune.ToUpperInvariant(first).ToString() + Lower(suffix[first.Utf16SequenceLength..]);
        }

        name = prefix + suffix;
        return true;
    }

    /// <summary>
    /// Converts query or fallback name text to invariant lower-case text.
    /// </summary>
    /// <param name="value">The name or query text to normalize.</param>
    /// <returns>The invariant lower-case text.</returns>
    internal static string Lower(string value)
        => value.ToLowerInvariant();
}
