using System.Text.RegularExpressions;
using System.Text;
using System.Globalization;
using System.Numerics;

namespace Ironmon.Tracker.Connection.Lookup;

/// <summary>
/// Indexes normal and eligible fusion names and caches query-specific native result ordering.
/// </summary>
/// <remarks>
/// Search rows retain original game pool order until an uncached query is matched and sorted.
/// Returned identifier assignments require authoritative game validation and annotation.
/// </remarks>
internal sealed partial class PokemonSearchCatalog
{
    private const string IdentityPattern = @"\A(?:b[0-9]*(?:h[0-9]*)?|[0-9]+h[0-9]*|h[0-9]+|[0-9]+)\z";
    private const string FormSuffix = ":0";
    private readonly Entry[] _entries;
    private readonly string _contextToken;
    private readonly Dictionary<string, int[]> _queries = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();
    private const int QueryCapacity = 64;

    /// <summary>
    /// Gets the game-supplied generation profile whose names are indexed.
    /// </summary>
    internal string GenerationProfileId { get; }

    /// <summary>
    /// Builds normalized search rows in original pool order from a game-supplied compact catalog.
    /// </summary>
    /// <remarks>
    /// Native lower-case name parts are used when supplied. Sorting occurs when an uncached
    /// query is searched; construction observes cancellation while enumerating fusion bodies.
    /// </remarks>
    /// <param name="payload">The authoritative game-owned names and eligibility bits.</param>
    /// <param name="cancellationToken">The token that cancels fusion-row enumeration during index preparation.</param>
    /// <exception cref="InvalidDataException">Thrown when the catalog schema, component dimensions, or eligibility pool are unsupported.</exception>
    /// <exception cref="FormatException">Thrown when the eligibility pool is not valid Base64 text.</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is observed during fusion-row enumeration.</exception>
    internal PokemonSearchCatalog(PokemonNameCatalogPayload payload, CancellationToken cancellationToken)
    {
        _ = new PokemonNameCatalog(payload);
        GenerationProfileId = payload.GenerationProfileId;
        _contextToken = payload.ContextToken;
        byte[] pool = Convert.FromBase64String(payload.PackedCustomFusionPool ?? throw new InvalidDataException("The name catalog has no eligible fusion pool."));
        int count = payload.NormalSpeciesCount;
        if (pool.Length != (checked(count * count) + 7) / 8)
            throw new InvalidDataException("The compact name pool has an invalid length.");

        int eligibleCount = pool.Sum(value => BitOperations.PopCount((uint)value));
        List<Entry> entries = new(payload.NormalSpecies.Count + eligibleCount);
        for (int index = 0; index < payload.NormalSpecies.Count; index++)
        {
            PokemonSearchMatch normal = payload.NormalSpecies[index];
            string name = payload.NormalizedNormalNames.Count == payload.NormalSpecies.Count ? payload.NormalizedNormalNames[index] : PokemonNameCatalog.Lower(normal.SpeciesName);
            string searchable = payload.NormalizedNormalSearchTexts.Count == payload.NormalSpecies.Count ? payload.NormalizedNormalSearchTexts[index] : PokemonNameCatalog.Lower(normal.SpeciesName + ' ' + normal.SpeciesId[..^FormSuffix.Length]);
            entries.Add(new Entry(normal.SpeciesId, name, searchable, false, GetSupplementarySortKey(name)));
        }

        bool nativeLowercase = payload.LowercasePrefixes.Count == count + 1 && payload.LowercaseTrimmedPrefixes.Count == count + 1 && payload.LowercaseSuffixes.Count == count + 1 && payload.LowercaseCapitalizedSuffixes.Count == count + 1;
        string?[] lowerPrefixes = new string?[count + 1];
        string?[] trimmedPrefixes = new string?[count + 1];
        string?[] lowerSuffixes = new string?[count + 1];
        string?[] capitalizedSuffixes = new string?[count + 1];
        int[] lastCharacters = new int[count + 1];
        int[] firstCharacters = new int[count + 1];
        bool[] prefixSpaces = new bool[count + 1];
        bool[] trimmedSpaces = new bool[count + 1];
        for (int id = 1; id <= count; id++)
        {
            string? prefix = payload.Prefixes[id];
            string? suffix = payload.Suffixes[id];
            if (prefix is not null)
            {
                string trimmed = prefix.Length == 0 ? prefix : prefix[..^(char.IsLowSurrogate(prefix[^1]) ? 2 : 1)];
                lastCharacters[id] = prefix.Length == 0 ? -1 : Rune.GetRuneAt(prefix, prefix.Length - (char.IsLowSurrogate(prefix[^1]) ? 2 : 1)).Value;
                prefixSpaces[id] = prefix.EndsWith(' ');
                trimmedSpaces[id] = trimmed.EndsWith(' ');
                lowerPrefixes[id] = nativeLowercase ? payload.LowercasePrefixes[id] : PokemonNameCatalog.Lower(prefix);
                trimmedPrefixes[id] = nativeLowercase ? payload.LowercaseTrimmedPrefixes[id] : PokemonNameCatalog.Lower(trimmed);
            }

            if (suffix is not null)
            {
                firstCharacters[id] = suffix.Length == 0 ? -2 : Rune.GetRuneAt(suffix, 0).Value;
                lowerSuffixes[id] = nativeLowercase ? payload.LowercaseSuffixes[id] : PokemonNameCatalog.Lower(suffix);
                if (nativeLowercase)
                    capitalizedSuffixes[id] = payload.LowercaseCapitalizedSuffixes[id];
                else if (payload.CapitalizedSuffixes.Count == count + 1)
                    capitalizedSuffixes[id] = PokemonNameCatalog.Lower(payload.CapitalizedSuffixes[id] ?? suffix);
                else
                    capitalizedSuffixes[id] = PokemonNameCatalog.Lower(suffix);
            }
        }

        for (int body = 1; body <= count; body++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int head = 1; head <= count; head++)
            {
                int position = (body - 1) * count + head - 1;
                if ((pool[position >> 3] & (1 << (position & 7))) == 0 || lowerPrefixes[head] is null || lowerSuffixes[body] is null)
                    continue;

                bool trimmed = lastCharacters[head] == firstCharacters[body];
                bool capitalize = trimmed ? trimmedSpaces[head] : prefixSpaces[head];
                string normalized = (trimmed ? trimmedPrefixes[head] : lowerPrefixes[head]) + (capitalize ? capitalizedSuffixes[body] : lowerSuffixes[body]);
                string identity = string.Create(CultureInfo.InvariantCulture, $"B{body}H{head}{FormSuffix}");
                entries.Add(new Entry(identity, normalized, string.Empty, true, GetSupplementarySortKey(normalized)));
            }
        }

        _entries = [.. entries];
    }

    /// <summary>
    /// Matches and pages a query using native prefix priority, name ordering, and fusion identity rules.
    /// </summary>
    /// <remarks>
    /// Cached queries reuse their ordered indices. Uncached matching periodically observes
    /// cancellation and checks it again after the synchronous native sort completes.
    /// Equal-key ordering follows the query-dependent native CRT result rather than a stable sort.
    /// </remarks>
    /// <param name="query">The trimmed query to normalize with invariant lower-case casing.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="limit">The maximum number of identifiers to return.</param>
    /// <param name="cancellationToken">The token that cancels uncached matching; cached page selection does not observe it.</param>
    /// <returns>A new assignment containing the ordered page identifiers, complete match count, and catalog context token.</returns>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is observed during uncached matching or after sorting.</exception>
    internal PokemonNameSearchAssignmentPayload Search(string query, int offset, int limit, CancellationToken cancellationToken)
    {
        string normalized = PokemonNameCatalog.Lower(query);
        int[]? indices;
        lock (_sync)
            _queries.TryGetValue(normalized, out indices);

        if (indices is null)
        {
            bool identities = IdentityRegex().IsMatch(normalized);
            List<int> matches = [];
            for (int index = 0; index < _entries.Length; index++)
            {
                if ((index & 255) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                Entry entry = _entries[index];
                bool match = entry.Fusion ? entry.Name.Contains(normalized, StringComparison.Ordinal) || identities && entry.Id.AsSpan(0, entry.Id.Length - FormSuffix.Length).Contains(normalized, StringComparison.OrdinalIgnoreCase) : entry.Searchable.Contains(normalized, StringComparison.Ordinal);
                if (!match)
                    continue;

                matches.Add(index);
            }

            indices = [.. matches];
            PokemonNameSearchSort.Sort(indices, (left, right) =>
            {
                int leftRank = _entries[left].Name.StartsWith(normalized, StringComparison.Ordinal) ? 0 : 1;
                int rightRank = _entries[right].Name.StartsWith(normalized, StringComparison.Ordinal) ? 0 : 1;
                if (leftRank != rightRank)
                    return leftRank.CompareTo(rightRank);

                Entry first = _entries[left];
                Entry second = _entries[right];
                if (first.Utf8SortKey is null && second.Utf8SortKey is null)
                    return string.CompareOrdinal(first.Name, second.Name);

                byte[] firstKey = first.Utf8SortKey ?? Encoding.UTF8.GetBytes(first.Name);
                byte[] secondKey = second.Utf8SortKey ?? Encoding.UTF8.GetBytes(second.Name);
                return firstKey.AsSpan().SequenceCompareTo(secondKey);
            });
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_queries.Count >= QueryCapacity)
                    _queries.Remove(_queries.Keys.First());
                _queries[normalized] = indices;
            }
        }

        return CreatePage(indices, offset, limit);
    }

    /// <summary>
    /// Returns a new assignment page when the query already has cached matching indices.
    /// </summary>
    /// <param name="query">The current trimmed query.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="limit">The maximum number of identifiers to return.</param>
    /// <param name="page">The new assignment on success, or null when the query has no cached match set.</param>
    /// <returns>Whether an ordered match set is already cached for the normalized query.</returns>
    internal bool TryGetCachedPage(string query, int offset, int limit, out PokemonNameSearchAssignmentPayload page)
    {
        int[]? indices;
        lock (_sync)
            _queries.TryGetValue(PokemonNameCatalog.Lower(query), out indices);

        if (indices is null)
        {
            page = null!;
            return false;
        }

        page = CreatePage(indices, offset, limit);
        return true;
    }

    /// <summary>
    /// Copies the requested identifiers from an ordered match set into a new assignment.
    /// </summary>
    /// <param name="indices">The matched indices in verified native order.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="limit">The maximum number of identifiers to return.</param>
    /// <returns>The page identifiers, complete match count, and context token for authoritative game annotation.</returns>
    private PokemonNameSearchAssignmentPayload CreatePage(int[] indices, int offset, int limit)
        => new() { ContextToken = _contextToken, Total = indices.Length, SpeciesIds = [.. indices.Skip(offset).Take(limit).Select(index => _entries[index].Id)] };

    /// <summary>
    /// Gets the generated matcher for normalized native species-identity queries.
    /// </summary>
    /// <returns>The shared culture-invariant identity matcher.</returns>
    [GeneratedRegex(IdentityPattern, RegexOptions.CultureInvariant)]
    private static partial Regex IdentityRegex();

    /// <summary>
    /// Provides UTF-8 ordering for names containing supplementary Unicode characters.
    /// </summary>
    /// <param name="name">The normalized native name.</param>
    /// <returns>The UTF-8 sort key when the name contains UTF-16 surrogates, or null when ordinal UTF-16 ordering suffices.</returns>
    private static byte[]? GetSupplementarySortKey(string name)
        => name.AsSpan().ContainsAnyInRange('\uD800', '\uDFFF') ? Encoding.UTF8.GetBytes(name) : null;

    /// <summary>
    /// Stores one indexed species and its normalized name and search metadata.
    /// </summary>
    /// <remarks>
    /// Initializes one search row before query matching and sorting.
    /// </remarks>
    /// <param name="Id">The stable species and form identifier.</param>
    /// <param name="Name">The normalized displayed name.</param>
    /// <param name="Searchable">The normalized normal name and identity text, or an empty string for a fusion row.</param>
    /// <param name="Fusion">Whether native fusion identity-query rules apply.</param>
    /// <param name="Utf8SortKey">The optional UTF-8 key for supplementary Unicode names.</param>
    private sealed record Entry(string Id, string Name, string Searchable, bool Fusion, byte[]? Utf8SortKey);
}
