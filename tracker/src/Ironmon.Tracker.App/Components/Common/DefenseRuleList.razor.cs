using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Renders compact effect labels with affected moves or recovery outcomes hidden until the label is clicked.
/// </summary>
public partial class DefenseRuleList
{
    /// <summary>
    /// Gets or sets the section heading.
    /// </summary>
    [Parameter] public string Heading { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the already privacy-filtered compact effects.
    /// </summary>
    [Parameter] public IReadOnlyList<DefenseEffectSnapshot> Effects { get; set; } = [];

    /// <summary>
    /// Gets one entry per available label, preserving independent recovery contributions.
    /// </summary>
    private IReadOnlyList<DefenseEffectSnapshot> VisibleEffects => [.. Effects.Where(effect => effect.Active != false && !string.IsNullOrWhiteSpace(effect.Label)).GroupBy(effect => effect.Label).Select(MergeEffects)];

    /// <summary>
    /// Combines labels and move names while retaining independent recovery contributions and their rounding.
    /// </summary>
    private static DefenseEffectSnapshot MergeEffects(IGrouping<string, DefenseEffectSnapshot> group)
    {
        return new DefenseEffectSnapshot
        {
            Label = group.Key,
            Active = group.Any(effect => effect.Active == true) ? true : null,
            HealingAmounts = [.. group.SelectMany(effect => effect.HealingAmounts)],
            Moves = [.. group.SelectMany(effect => effect.Moves).Distinct().Order()]
        };
    }
}
