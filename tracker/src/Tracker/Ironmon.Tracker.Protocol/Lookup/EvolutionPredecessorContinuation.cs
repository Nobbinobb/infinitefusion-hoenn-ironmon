namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Describes whether another generated-predecessor page is known or may require more scanning.
/// </summary>
public enum EvolutionPredecessorContinuation
{
    /// <summary>
    /// No later generated predecessor exists.
    /// </summary>
    Complete = 0,

    /// <summary>
    /// At least one later generated predecessor is already known.
    /// </summary>
    Available = 1,

    /// <summary>
    /// The current page is full but later results have not yet been proven or excluded.
    /// </summary>
    Unknown = 2
}
