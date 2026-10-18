using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Renders compact chips for the currently modified battle stat stages.
/// </summary>
public partial class BattleStatStageChips
{
    /// <summary>
    /// Gets or sets the current battle stat stages.
    /// </summary>
    [Parameter]
    public BattleStatStagesSnapshot? Stages { get; set; }

    /// <summary>
    /// Gets the nonzero stages in tracker display order.
    /// </summary>
    /// <returns>The visible stat labels, stages, and multiplier rules.</returns>
    private IReadOnlyList<(string Name, int Stage, bool UsesAccuracyFormula)> GetVisibleStages()
    {
        if (Stages is null)
            return [];

        (string Name, int Stage, bool UsesAccuracyFormula)[] stages =
        [
            ("SPE", Stages.Speed, false),
            ("ATK", Stages.Attack, false),
            ("SPA", Stages.SpecialAttack, false),
            ("DEF", Stages.Defense, false),
            ("SPD", Stages.SpecialDefense, false),
            ("ACC", Stages.Accuracy, true),
            ("EVA", Stages.Evasion, true)
        ];

        return [.. stages.Where(stage => stage.Stage != 0)];
    }
}
