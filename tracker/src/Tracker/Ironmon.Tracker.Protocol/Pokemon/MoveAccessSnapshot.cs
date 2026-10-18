namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Groups every generated move-access channel for one Pokemon.
/// </summary>
public sealed class MoveAccessSnapshot
{
    /// <summary>
    /// Initializes an empty move-access snapshot for protocol serialization.
    /// </summary>
    public MoveAccessSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the chronological level-up and evolution learnset.
    /// </summary>
    public IReadOnlyList<MoveAccessEntrySnapshot> Learnset { get; init; } = [];

    /// <summary>
    /// Gets or initializes the complete generated Egg move list.
    /// </summary>
    public IReadOnlyList<MoveAccessEntrySnapshot> EggMoves { get; init; } = [];

    /// <summary>
    /// Gets or initializes generated TM and TR compatibility rows.
    /// </summary>
    public IReadOnlyList<MoveAccessEntrySnapshot> MachineMoves { get; init; } = [];

    /// <summary>
    /// Gets or initializes supported ordinary and specialized tutor rows.
    /// </summary>
    public IReadOnlyList<MoveAccessEntrySnapshot> TutorMoves { get; init; } = [];

    /// <summary>
    /// Gets or initializes the size of the abstract ordinary-tutor compatibility list.
    /// </summary>
    public int OrdinaryTutorAbstractCount { get; init; }

    /// <summary>
    /// Gets or initializes the number of abstract ordinary-tutor moves offered in this run.
    /// </summary>
    public int OrdinaryTutorSupportedCount { get; init; }
}
