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
    /// <returns>The visible stat labels and stages.</returns>
    private IReadOnlyList<(string Name, int Stage)> GetVisibleStages()
    {
        if (Stages is null)
            return [];

        (string Name, int Stage)[] stages =
        [
            ("SPE", Stages.Speed),
            ("ATK", Stages.Attack),
            ("SPA", Stages.SpecialAttack),
            ("DEF", Stages.Defense),
            ("SPD", Stages.SpecialDefense)
        ];

        return [.. stages.Where(stage => stage.Stage != 0)];
    }
}
