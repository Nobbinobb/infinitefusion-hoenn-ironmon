using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Shares the ordered stat layout and separate battle stages across migrated cards.
/// </summary>
public partial class ObsidianStatSection
{
    /// <summary>
    /// Gets or sets the section's visible and accessible heading.
    /// </summary>
    [Parameter]
    public string Heading { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets optional help actions beside the heading.
    /// </summary>
    [Parameter]
    public RenderFragment? HeadingActions { get; set; }

    /// <summary>
    /// Gets or sets the ordered stat values or manual annotations.
    /// </summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Gets or sets current battle stages independently of values or annotations.
    /// </summary>
    [Parameter]
    public BattleStatStagesSnapshot? Stages { get; set; }

    /// <summary>
    /// Determines whether a separate battle-stage line is needed.
    /// </summary>
    /// <returns>Whether at least one supported battle stage is nonzero.</returns>
    private bool HasStages()
    {
        return Stages is not null
            && (Stages.Attack != 0 || Stages.Defense != 0 || Stages.SpecialAttack != 0
                || Stages.SpecialDefense != 0 || Stages.Speed != 0 || Stages.Accuracy != 0 || Stages.Evasion != 0);
    }
}
