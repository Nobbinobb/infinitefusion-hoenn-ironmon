namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Identifies the game-owned Pokemon source inspected in debug mode.
/// </summary>
public enum DebugPokemonTarget
{
    /// <summary>
    /// Inspects the initialized player Pokemon used by the live tracker.
    /// </summary>
    Player = 0,

    /// <summary>
    /// Inspects an active opposing battler by position.
    /// </summary>
    Enemy = 1,

    /// <summary>
    /// Inspects a player party member by position.
    /// </summary>
    Party = 2
}
