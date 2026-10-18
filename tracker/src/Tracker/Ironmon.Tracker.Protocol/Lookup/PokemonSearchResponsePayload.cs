namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Returns a Pokemon search match page or a requested compact name catalog.
/// </summary>
public sealed class PokemonSearchResponsePayload
{
    /// <summary>
    /// Initializes an empty Pokemon search response for protocol serialization.
    /// </summary>
    public PokemonSearchResponsePayload()
    {
    }

    /// <summary>
    /// Gets or initializes the matching Pokemon in display order, or an empty list for a catalog-only response.
    /// </summary>
    public IReadOnlyList<PokemonSearchMatch> Matches { get; init; } = [];

    /// <summary>
    /// Gets or initializes the total number of matches before paging, or zero for a catalog-only response.
    /// </summary>
    public int Total { get; init; }

    /// <summary>
    /// Gets or initializes the compact name catalog for a catalog-only request, or null for a match-page response.
    /// </summary>
    public PokemonNameCatalogPayload? NameCatalog { get; init; }
}
