namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Identifies the generated evolution candidate list being requested.
/// </summary>
public enum EvolutionCandidateSide
{
    /// <summary>
    /// Identifies a normal Pokemon's candidate list.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Identifies candidates activated by a fusion's head.
    /// </summary>
    Head = 1,

    /// <summary>
    /// Identifies candidates activated by a fusion's body.
    /// </summary>
    Body = 2
}
