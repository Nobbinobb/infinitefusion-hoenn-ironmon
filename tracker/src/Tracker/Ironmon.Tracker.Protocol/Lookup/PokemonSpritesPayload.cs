namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Requests a bounded batch of public species artwork without generated gameplay information.
/// </summary>
public sealed class PokemonSpritesRequestPayload
{
    /// <summary>
    /// Gets or initializes up to eight canonical species and form identifiers.
    /// </summary>
    public required IReadOnlyList<string> SpeciesIds { get; init; }
}

/// <summary>
/// Returns materialized local artwork paths keyed by the requested species identity.
/// </summary>
public sealed class PokemonSpritesResponsePayload
{
    /// <summary>
    /// Gets or initializes the available game-relative image paths, with null for unavailable artwork.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Sprites { get; init; } = new Dictionary<string, string?>();
}
