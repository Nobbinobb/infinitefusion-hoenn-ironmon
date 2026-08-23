namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Identifies the current proof state for one run-specific Pokemon.
/// </summary>
public enum PokemonObtainabilityStatus
{
    /// <summary>
    /// Indicates that authored source coverage is insufficient for a definitive result.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Indicates that the shared player-fusion pass is still running.
    /// </summary>
    Calculating = 1,

    /// <summary>
    /// Indicates that at least one valid permanent acquisition path was proven.
    /// </summary>
    Obtainable = 2,

    /// <summary>
    /// Indicates that the complete calculation found no valid permanent acquisition path.
    /// </summary>
    Unobtainable = 3
}
