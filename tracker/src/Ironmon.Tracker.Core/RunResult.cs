namespace Ironmon.Tracker.Core;

/// <summary>
/// Identifies how an Ironmon run ended.
/// </summary>
public enum RunResult
{
    /// <summary>
    /// Indicates that the run has not ended.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Indicates that the player lost the run.
    /// </summary>
    Lost = 1,

    /// <summary>
    /// Indicates that the player completed the run successfully.
    /// </summary>
    Won = 2,

    /// <summary>
    /// Indicates that the player deliberately abandoned the run.
    /// </summary>
    Abandoned = 3
}
