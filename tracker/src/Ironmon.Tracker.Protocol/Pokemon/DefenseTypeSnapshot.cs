namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Describes one incoming attack type's baseline matchup and combined physical and special factor ranges.
/// </summary>
/// <remarks>
/// Combined factors are relative values before damage-formula rounding, not exact HP-loss predictions. Missing combined factors fall back to Multiplier in the view.
/// </remarks>
public sealed class DefenseTypeSnapshot
{
    /// <summary>
    /// Initializes an empty defense payload for serialization.
    /// </summary>
    public DefenseTypeSnapshot()
    {
    }

    /// <summary>
    /// The attack type identifier.
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// The localized attack type name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The unmodified multiplier of the current defensive types.
    /// </summary>
    public decimal BaseMultiplier { get; init; }

    /// <summary>
    /// The matchup after known immunities and type-chart overrides.
    /// </summary>
    public decimal Multiplier { get; init; }

    /// <summary>
    /// Gets the minimum combined physical damage factor.
    /// </summary>
    public decimal? PhysicalMin { get; init; }

    /// <summary>
    /// Gets the maximum combined physical damage factor.
    /// </summary>
    public decimal? PhysicalMax { get; init; }

    /// <summary>
    /// Gets the minimum combined special damage factor.
    /// </summary>
    public decimal? SpecialMin { get; init; }

    /// <summary>
    /// Gets the maximum combined special damage factor.
    /// </summary>
    public decimal? SpecialMax { get; init; }

    /// <summary>
    /// Gets supporting rules included in the combined factors; their sources and explanations are not displayed.
    /// </summary>
    public IReadOnlyList<DefenseRuleSnapshot> Adjustments { get; init; } = [];

    /// <summary>
    /// Gets supporting move-specific effects that cannot be applied to a whole attack type and are not disclosed by its UI row.
    /// </summary>
    public IReadOnlyList<DefenseRuleSnapshot> Exceptions { get; init; } = [];
}
