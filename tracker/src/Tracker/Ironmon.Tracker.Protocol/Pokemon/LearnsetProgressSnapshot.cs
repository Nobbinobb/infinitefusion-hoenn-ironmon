namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Describes player progress through the complete level-up learnset.
/// </summary>
public sealed class LearnsetProgressSnapshot
{
    /// <summary>
    /// Initializes empty learnset progress.
    /// </summary>
    public LearnsetProgressSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the number of level-up moves recorded as learned.
    /// </summary>
    public int LearnedMoves { get; init; }

    /// <summary>
    /// Gets or initializes the total number of unique level-up moves.
    /// </summary>
    public int MaximumMoves { get; init; }

    /// <summary>
    /// Gets or initializes the next level that offers a move, or null at the end of the learnset.
    /// </summary>
    public int? NextMoveLevel { get; init; }
}
