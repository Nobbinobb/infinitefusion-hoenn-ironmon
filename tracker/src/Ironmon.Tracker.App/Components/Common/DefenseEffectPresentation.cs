namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Shares compact defense-effect merging between migrated and legacy views.
/// </summary>
internal static class DefenseEffectPresentation
{
    /// <summary>
    /// Combines public labels while preserving independent recovery contributions and their rounding.
    /// </summary>
    /// <param name="effects">The privacy-filtered protection or recovery effects.</param>
    /// <returns>One visible entry per label with all independent recovery contributions.</returns>
    public static IReadOnlyList<DefenseEffectSnapshot> Merge(IReadOnlyList<DefenseEffectSnapshot> effects)
    {
        return [.. effects.Where(effect => effect.Active != false && !string.IsNullOrWhiteSpace(effect.Label))
            .GroupBy(effect => effect.Label).Select(MergeGroup)];
    }

    /// <summary>
    /// Deduplicates public move names without collapsing independent healing amounts.
    /// </summary>
    /// <param name="group">The effects sharing one visible label.</param>
    /// <returns>The combined effect with unique moves and preserved healing contributions.</returns>
    private static DefenseEffectSnapshot MergeGroup(IGrouping<string, DefenseEffectSnapshot> group)
    {
        return new()
        {
            Label = group.Key,
            Active = group.Any(effect => effect.Active == true) ? true : null,
            HealingAmounts = [.. group.SelectMany(effect => effect.HealingAmounts)],
            Moves = [.. group.SelectMany(effect => effect.Moves).Distinct().Order()]
        };
    }
}
