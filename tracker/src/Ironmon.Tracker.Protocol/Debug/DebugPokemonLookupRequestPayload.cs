using Ironmon.Tracker.Protocol.Lookup;

namespace Ironmon.Tracker.Protocol.Debug;

/// <summary>
/// Requests generated information for one Pokemon in the active debug run.
/// </summary>
public sealed class DebugPokemonLookupRequestPayload
{
    /// <summary>
    /// Initializes an empty active-run lookup request for protocol serialization.
    /// </summary>
    public DebugPokemonLookupRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the selected stable species and form identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the independently requested information section.
    /// </summary>
    public PokemonLookupSection Section { get; init; } = PokemonLookupSection.Overview;
}
