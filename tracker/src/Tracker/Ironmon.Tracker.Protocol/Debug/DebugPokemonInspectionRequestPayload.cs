using Ironmon.Tracker.Protocol.Lookup;

namespace Ironmon.Tracker.Protocol.Debug;

/// <summary>
/// Requests one game-owned Pokemon inspector snapshot in authorized debug mode.
/// </summary>
public sealed class DebugPokemonInspectionRequestPayload
{
    /// <summary>
    /// Initializes an empty debug inspection request for protocol serialization.
    /// </summary>
    public DebugPokemonInspectionRequestPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the Pokemon source to inspect.
    /// </summary>
    public DebugPokemonTarget Target { get; init; }

    /// <summary>
    /// Gets or initializes the independently requested inspector section.
    /// </summary>
    public PokemonLookupSection Section { get; init; } = PokemonLookupSection.Overview;

    /// <summary>
    /// Gets or initializes the enemy battler position when inspecting an enemy.
    /// </summary>
    public int? EnemyPosition { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based party position when inspecting the party.
    /// </summary>
    public int? PartyIndex { get; init; }
}
