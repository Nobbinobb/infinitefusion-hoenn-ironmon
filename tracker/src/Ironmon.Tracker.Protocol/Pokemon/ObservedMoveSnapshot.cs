namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Describes one legally observable level-up move and its display metadata.
/// </summary>
public sealed class ObservedMoveSnapshot
{
    /// <summary>
    /// Initializes an empty observed move snapshot for protocol serialization.
    /// </summary>
    public ObservedMoveSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the stable move identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets or initializes the localized move name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets or initializes the level at which the move is learned.
    /// </summary>
    public int LearnedLevel { get; init; }

    /// <summary>
    /// Gets or initializes the stable position within the generated learnset.
    /// </summary>
    public int LearnOrder { get; init; }

    /// <summary>
    /// Gets or initializes the learn source identifier.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets or initializes the observation origin identifier.
    /// </summary>
    public required string Origin { get; init; }

    /// <summary>
    /// Gets or initializes the stable move type identifier.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets or initializes the move damage category.
    /// </summary>
    public MoveCategory Category { get; init; }

    /// <summary>
    /// Gets or initializes the localized move description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the move's base power.
    /// </summary>
    public int Power { get; init; }

    /// <summary>
    /// Gets or initializes the privacy-safe display for conditional or nonstandard power.
    /// </summary>
    public MovePowerPresentationSnapshot? PowerPresentation { get; init; }

    /// <summary>
    /// Gets or initializes the move's accuracy.
    /// </summary>
    public int Accuracy { get; init; }

    /// <summary>
    /// Gets or initializes the move's total PP.
    /// </summary>
    public int TotalPp { get; init; }

    /// <summary>
    /// Gets or initializes the most recently observed remaining PP when known.
    /// </summary>
    public int? PpAfterUse { get; init; }
}
