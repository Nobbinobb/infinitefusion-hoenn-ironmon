namespace Ironmon.Tracker.Collection;

/// <summary>
/// Identifies the recorded transition into one retained runner stage.
/// </summary>
public enum RunPokemonTransition
{
    /// <summary>
    /// The selected starter entered the run.
    /// </summary>
    Starter = 0,
    /// <summary>
    /// A fusion pivot replaced the runner.
    /// </summary>
    FusionPivot = 1,
    /// <summary>
    /// A reversed fusion replaced the runner.
    /// </summary>
    ReversePivot = 2,
    /// <summary>
    /// An unfused component replaced the runner.
    /// </summary>
    UnfusionPivot = 3,
    /// <summary>
    /// Another retained Pokemon replaced the runner.
    /// </summary>
    Pivot = 4,
    /// <summary>
    /// The same individual evolved or devolved.
    /// </summary>
    Evolution = 5,
    /// <summary>
    /// The same individual's persistent form changed.
    /// </summary>
    FormChange = 6,
    /// <summary>
    /// Recording began without evidence of the earlier acquisition.
    /// </summary>
    FirstRecorded = 7,
    /// <summary>
    /// The same individual changed species through another recorded action.
    /// </summary>
    SpeciesChange = 8
}

/// <summary>
/// Presents one continuous retained runner stage from exact archived ownership evidence.
/// </summary>
/// <remarks>
/// Initializes an immutable entry without regenerating the run or inferring missing history.
/// </remarks>
/// <param name="Order">The one-based chronological stage number; newest entries have the highest number.</param>
/// <param name="SpeciesId">The game lookup species and form identifier.</param>
/// <param name="Name">The recorded Pokemon name, including its nickname when one was used.</param>
/// <param name="SpritePath">The recorded game-relative sprite, or null when unavailable.</param>
/// <param name="HighestLevel">The highest recorded level during this continuous stage.</param>
/// <param name="Transition">The recorded transition into this stage.</param>
public sealed record RunPokemonHistoryEntry(int Order, string SpeciesId, string Name, string? SpritePath, int HighestLevel, RunPokemonTransition Transition);
