using Ironmon.Tracker.Protocol.Live;
using Ironmon.Tracker.Protocol.Pokemon;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Renders visible stats with nature colors and separate battle stages.
/// </summary>
public partial class ObsidianStatRow
{
    private const string _positiveClass = "obsidian-positive";
    private const string _negativeClass = "obsidian-negative";

    /// <summary>
    /// Gets or sets the ordered, visibility-filtered stat values.
    /// </summary>
    [Parameter]
    public IReadOnlyList<TrackerStatValue> Stats { get; set; } = [];

    /// <summary>
    /// Gets or sets the current battle stages.
    /// </summary>
    [Parameter]
    public BattleStatStagesSnapshot? Stages { get; set; }

    /// <summary>
    /// Chooses the nature color without redundant arrows.
    /// </summary>
    /// <param name="adjustment">The known nature adjustment for the stat.</param>
    /// <returns>The positive or negative CSS class, or null for a neutral stat.</returns>
    private static string? GetColor(StatAdjustment adjustment) => adjustment switch
    {
        StatAdjustment.Increased => _positiveClass,
        StatAdjustment.Decreased => _negativeClass,
        _ => null
    };
}

/// <summary>
/// Holds one visible stat for any migrated card.
/// </summary>
/// <remarks>
/// Initializes one visible stat from its label, formatted value, and known nature adjustment.
/// </remarks>
/// <param name="Label">The stat abbreviation.</param>
/// <param name="Value">The visible value or unknown placeholder.</param>
/// <param name="Adjustment">The known nature adjustment.</param>
public sealed record TrackerStatValue(string Label, string Value, StatAdjustment Adjustment = StatAdjustment.Neutral);
