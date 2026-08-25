namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Identifies the current proof state for one run-specific Pokemon.
/// </summary>
public enum PokemonObtainabilityStatus
{
    /// <summary>
    /// Indicates that the finite shared proof calculation is still running.
    /// </summary>
    Calculating = 0,

    /// <summary>
    /// Indicates that at least one valid permanent acquisition path was proven.
    /// </summary>
    Obtainable = 1,

    /// <summary>
    /// Indicates that the complete calculation found no valid permanent acquisition path.
    /// </summary>
    Unobtainable = 2
}
