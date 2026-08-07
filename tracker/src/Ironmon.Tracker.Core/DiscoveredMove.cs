namespace Ironmon.Tracker.Core;

/// <summary>
/// Represents one move legitimately discovered for a species during a run.
/// </summary>
public sealed class DiscoveredMove
{
    /// <summary>
    /// Initializes a discovered move.
    /// </summary>
    /// <param name="moveId">The stable move identifier.</param>
    /// <param name="learnedLevel">The level at which the move is learned.</param>
    /// <param name="learnOrder">The stable order within the generated learnset.</param>
    /// <param name="learnSource">The source through which the move is learned.</param>
    /// <param name="discoveryOrigin">The action that revealed the move.</param>
    /// <exception cref="ArgumentException">Thrown when the move identifier is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the learned level or learn order is negative.
    /// </exception>
    public DiscoveredMove(string moveId, int learnedLevel, int learnOrder, MoveLearnSource learnSource, MoveDiscoveryOrigin discoveryOrigin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moveId);
        ArgumentOutOfRangeException.ThrowIfNegative(learnedLevel);
        ArgumentOutOfRangeException.ThrowIfNegative(learnOrder);

        MoveId = moveId;
        LearnedLevel = learnedLevel;
        LearnOrder = learnOrder;
        LearnSource = learnSource;
        DiscoveryOrigin = discoveryOrigin;
    }

    /// <summary>
    /// Gets the stable move identifier.
    /// </summary>
    public string MoveId { get; }

    /// <summary>
    /// Gets the level at which the move is learned.
    /// </summary>
    public int LearnedLevel { get; }

    /// <summary>
    /// Gets the stable order within the generated learnset.
    /// </summary>
    public int LearnOrder { get; }

    /// <summary>
    /// Gets the source through which the move is learned.
    /// </summary>
    public MoveLearnSource LearnSource { get; }

    /// <summary>
    /// Gets the observable action that revealed the move.
    /// </summary>
    public MoveDiscoveryOrigin DiscoveryOrigin { get; }
}
