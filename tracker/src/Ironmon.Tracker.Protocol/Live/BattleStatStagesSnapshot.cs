namespace Ironmon.Tracker.Protocol.Live;

/// <summary>
/// Describes the current battle stages for stats that support boosts.
/// </summary>
public sealed class BattleStatStagesSnapshot
{
    /// <summary>
    /// Initializes an empty neutral-stage snapshot.
    /// </summary>
    public BattleStatStagesSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the current Attack stage.
    /// </summary>
    public int Attack { get; init; }

    /// <summary>
    /// Gets or initializes the current Defense stage.
    /// </summary>
    public int Defense { get; init; }

    /// <summary>
    /// Gets or initializes the current Special Attack stage.
    /// </summary>
    public int SpecialAttack { get; init; }

    /// <summary>
    /// Gets or initializes the current Special Defense stage.
    /// </summary>
    public int SpecialDefense { get; init; }

    /// <summary>
    /// Gets or initializes the current Speed stage.
    /// </summary>
    public int Speed { get; init; }

    /// <summary>
    /// Gets or initializes the current accuracy stage.
    /// </summary>
    public int Accuracy { get; init; }

    /// <summary>
    /// Gets or initializes the current evasion stage.
    /// </summary>
    public int Evasion { get; init; }
}
