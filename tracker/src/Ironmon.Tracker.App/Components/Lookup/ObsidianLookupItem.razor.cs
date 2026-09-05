using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents pickup progress and category icons only for disclosed item identities.
/// </summary>
public partial class ObsidianLookupItem
{
    private const string _hpRecovery = "hp_recovery";
    private const string _statusRecovery = "status_pp_recovery";
    private const string _utility = "general_utility";
    private const string _evolution = "evolution";
    private const string _ball = "poke_ball";
    private const string _tm = "tm";
    private const string _battle = "battle_consumable";
    private const string _held = "held_combat";
    private const string _categoryKeyPrefix = "Lookup.Items.Category.";

    /// <summary>
    /// Gets or sets the pickup entry with its revealed identities.
    /// </summary>
    [Parameter, EditorRequired]
    public AreaItemEntryPayload Item { get; set; } = null!;

    /// <summary>
    /// Maps the game's item-weighting category to its approved outline icon.
    /// </summary>
    /// <param name="category">The disclosed category, or null for older peers and persisted entries.</param>
    /// <returns>The category icon, with a general item fallback.</returns>
    private static ObsidianIconKind CategoryIcon(string? category) => category switch
    {
        _hpRecovery => ObsidianIconKind.HeartPulse,
        _statusRecovery => ObsidianIconKind.Pill,
        _evolution => ObsidianIconKind.EvolutionStone,
        _ball => ObsidianIconKind.Ball,
        _tm => ObsidianIconKind.Disc,
        _battle => ObsidianIconKind.Lightning,
        _held => ObsidianIconKind.Swords,
        _ => ObsidianIconKind.Backpack
    };

    /// <summary>
    /// Gets a localized category label without displaying protocol keys.
    /// </summary>
    /// <param name="category">The disclosed category.</param>
    /// <returns>The localized category label.</returns>
    private string CategoryLabel(string? category)
    {
        string key = category is _hpRecovery or _statusRecovery or _utility or _evolution or _ball or _tm or _battle or _held ? category : _utility;
        return Text[_categoryKeyPrefix + key];
    }
}
