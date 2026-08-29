namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Identifies the compact indicator shown beside a move's displayed power.
/// </summary>
public enum MovePowerIndicator
{
    /// <summary>
    /// No indicator is shown.
    /// </summary>
    None = 0,

    /// <summary>
    /// The displayed base value can change when a turn-order condition is met.
    /// </summary>
    Conditional = 1,

    /// <summary>
    /// The move has a variable number of strikes.
    /// </summary>
    MultiHit = 2,

    /// <summary>
    /// The value represents direct HP damage rather than base power.
    /// </summary>
    FixedDamage = 3
}

/// <summary>
/// Identifies the structured power detail shown in the move dialog.
/// </summary>
public enum MovePowerDetailsKind
{
    /// <summary>
    /// No structured power detail is available.
    /// </summary>
    None = 0,

    /// <summary>
    /// The move has a list of possible outcomes.
    /// </summary>
    Outcomes = 1,

    /// <summary>
    /// The move deals a calculated range of direct HP damage.
    /// </summary>
    Range = 2,

    /// <summary>
    /// The move has sequential escalating hits with accuracy checked per hit.
    /// </summary>
    TripleKick = 3
}

/// <summary>
/// Identifies one row in a structured move-power detail.
/// </summary>
public enum MovePowerOutcomeKind
{
    /// <summary>
    /// The row is a possible power value.
    /// </summary>
    Power = 0,

    /// <summary>
    /// The row is a healing outcome.
    /// </summary>
    Healing = 1,

    /// <summary>
    /// The row is a minimum-to-maximum direct-damage range.
    /// </summary>
    Range = 2,

    /// <summary>
    /// The row is a possible hit count and cumulative power.
    /// </summary>
    Hits = 3
}

/// <summary>
/// Describes the tracker-ready display for a conditional or nonstandard move power.
/// </summary>
public sealed class MovePowerPresentationSnapshot
{
    /// <summary>
    /// Initializes an empty move-power presentation for protocol serialization.
    /// </summary>
    public MovePowerPresentationSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the exact compact text shown in the power column.
    /// </summary>
    public required string Display { get; init; }

    /// <summary>
    /// Gets or initializes the compact semantic indicator.
    /// </summary>
    public MovePowerIndicator Indicator { get; init; }

    /// <summary>
    /// Gets or initializes the structured-detail layout.
    /// </summary>
    public MovePowerDetailsKind DetailsKind { get; init; }

    /// <summary>
    /// Gets or initializes whether sequential-hit accuracy is checked for every hit.
    /// </summary>
    public bool AccuracyCheckedPerHit { get; init; }

    /// <summary>
    /// Gets or initializes the structured outcome rows.
    /// </summary>
    public IReadOnlyList<MovePowerOutcomeSnapshot> Outcomes { get; init; } = [];
}

/// <summary>
/// Describes one possible outcome for a nonstandard move.
/// </summary>
public sealed class MovePowerOutcomeSnapshot
{
    /// <summary>
    /// Initializes an empty move-power outcome for protocol serialization.
    /// </summary>
    public MovePowerOutcomeSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the outcome kind.
    /// </summary>
    public MovePowerOutcomeKind Kind { get; init; }

    /// <summary>
    /// Gets or initializes the cumulative power when applicable.
    /// </summary>
    public int? Power { get; init; }

    /// <summary>
    /// Gets or initializes the hit count when applicable.
    /// </summary>
    public int? Hits { get; init; }

    /// <summary>
    /// Gets or initializes the outcome chance as a percentage.
    /// </summary>
    public decimal? ChancePercent { get; init; }

    /// <summary>
    /// Gets or initializes the minimum direct HP damage when applicable.
    /// </summary>
    public int? Minimum { get; init; }

    /// <summary>
    /// Gets or initializes the maximum direct HP damage when applicable.
    /// </summary>
    public int? Maximum { get; init; }

    /// <summary>
    /// Gets or initializes the target-HP percentage restored by a healing outcome.
    /// </summary>
    public int? HealingPercent { get; init; }
}
