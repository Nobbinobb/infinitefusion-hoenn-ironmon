using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents area progress, separating authored slots from chance fusions when the peer provides both totals.
/// </summary>
public partial class ObsidianLookupProgress
{
    private const string _defeatedKey = "Lookup.Areas.Defeated";
    private const string _encounteredKey = "Lookup.Areas.Encountered";
    private const string _collectedKey = "Lookup.Areas.Collected";

    /// <summary>
    /// Gets or sets the tracker-counted area summary.
    /// </summary>
    [Parameter, EditorRequired]
    public AreaSummaryPayload Area { get; set; } = null!;

    /// <summary>
    /// Gets or sets the displayed content category.
    /// </summary>
    [Parameter]
    public AreaContentCategory Category { get; set; }

    /// <summary>
    /// Gets the combined completion count for ordinary summaries and older peers.
    /// </summary>
    private int Completed => Category switch
    {
        AreaContentCategory.Trainer => Area.TrainerDefeated,
        AreaContentCategory.Item => Area.ItemsCollected,
        _ => Area.Encountered
    };

    /// <summary>
    /// Gets the combined category total.
    /// </summary>
    private int Total => Category switch
    {
        AreaContentCategory.Trainer => Area.TrainerTotal,
        AreaContentCategory.Item => Area.ItemTotal,
        _ => Area.EncounterTotal
    };

    /// <summary>
    /// Gets the localized completion label.
    /// </summary>
    private string ProgressLabel => Text[Category switch
    {
        AreaContentCategory.Trainer => _defeatedKey,
        AreaContentCategory.Item => _collectedKey,
        _ => _encounteredKey
    }];
}
