namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Describes a compact defense without provenance or explanatory prose.
/// </summary>
public sealed class DefenseEffectSnapshot
{
    /// <summary>
    /// Gets a protection label or recovery trigger, without its source or recovery outcome.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Gets HP, cure, duration, or stage-restoration outcomes disclosed when expanded; duplicate values represent independent contributions.
    /// </summary>
    public IReadOnlyList<string> HealingAmounts { get; init; } = [];

    /// <summary>
    /// Gets whether the effect is available; false entries are hidden and null entries are marked conditional.
    /// </summary>
    /// <remarks>
    /// A recovery trigger names when healing can occur; true does not assert that weather, missing HP, or every healing prerequisite currently permits recovery.
    /// </remarks>
    public bool? Active { get; init; }

    /// <summary>
    /// Gets public affected move names, never a concealed moveset.
    /// </summary>
    public IReadOnlyList<string> Moves { get; init; } = [];
}
