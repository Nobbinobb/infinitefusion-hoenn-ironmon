namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Describes a known damage factor applying to every attack type, before damage rounding.
/// </summary>
public sealed class DefenseAllTypeEffectSnapshot
{
    /// <summary>
    /// Initializes an empty all-type effect snapshot for protocol serialization.
    /// </summary>
    public DefenseAllTypeEffectSnapshot()
    {
    }

    /// <summary>
    /// Gets the game-provided public effect label.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Gets the affected category, or null for both categories.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Gets the relative damage factor.
    /// </summary>
    public decimal Factor { get; init; } = 1;

    /// <summary>
    /// Gets whether activation depends on an unresolved condition.
    /// </summary>
    public bool Conditional { get; init; }
}
