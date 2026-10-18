namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Describes one generated move-access row and its acquisition source.
/// </summary>
public sealed class MoveAccessEntrySnapshot
{
    /// <summary>
    /// Initializes an empty move-access row for protocol serialization.
    /// </summary>
    public MoveAccessEntrySnapshot()
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
    public int? LearnedLevel { get; init; }

    /// <summary>
    /// Gets or initializes the stable order within its access channel.
    /// </summary>
    public int LearnOrder { get; init; }

    /// <summary>
    /// Gets or initializes the access-channel identifier.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets or initializes the component or fusion source label.
    /// </summary>
    public required string SourceLabel { get; init; }

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
    /// Gets or initializes the move's accuracy.
    /// </summary>
    public int Accuracy { get; init; }

    /// <summary>
    /// Gets or initializes the move's total PP.
    /// </summary>
    public int TotalPp { get; init; }

    /// <summary>
    /// Gets or initializes the machine item identifier when applicable.
    /// </summary>
    public string? ItemId { get; init; }

    /// <summary>
    /// Gets or initializes the localized machine item name when applicable.
    /// </summary>
    public string? ItemName { get; init; }

    /// <summary>
    /// Gets or initializes the stable tutor slot identifier when applicable.
    /// </summary>
    public string? TutorId { get; init; }

    /// <summary>
    /// Gets or initializes the tutor location or catalog name when applicable.
    /// </summary>
    public string? TutorName { get; init; }
}
