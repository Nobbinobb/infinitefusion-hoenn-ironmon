namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Describes one move currently known by the active player Pokemon.
/// </summary>
public sealed class PlayerMoveSnapshot
{
    /// <summary>
    /// Initializes an empty player move snapshot for protocol serialization.
    /// </summary>
    public PlayerMoveSnapshot()
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
    /// Gets or initializes the current PP.
    /// </summary>
    public int CurrentPp { get; init; }

    /// <summary>
    /// Gets or initializes the maximum PP after PP increases.
    /// </summary>
    public int TotalPp { get; init; }

    /// <summary>
    /// Gets or initializes the base power, where zero represents a status move.
    /// </summary>
    public int Power { get; init; }

    /// <summary>
    /// Gets or initializes the accuracy, where zero represents an always-hit move.
    /// </summary>
    public int Accuracy { get; init; }
}
