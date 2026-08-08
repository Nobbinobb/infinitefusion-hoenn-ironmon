namespace Ironmon.Tracker.Protocol.Live;

/// <summary>
/// Identifies the player Pokémon whose move menu was opened for the first time after send-out.
/// </summary>
public sealed class PlayerMoveMenuOpenedPayload
{
    /// <summary>
    /// Initializes an empty move-menu payload for protocol serialization.
    /// </summary>
    public PlayerMoveMenuOpenedPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the stable individual Pokémon identifier.
    /// </summary>
    public required string PokemonId { get; init; }
}
