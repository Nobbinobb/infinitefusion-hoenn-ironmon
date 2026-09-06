namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Maps the shared item categories to the approved icons and localized label keys.
/// </summary>
public static class ItemCategoryPresentation
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
    /// Maps the game's item-weighting category to its approved outline icon.
    /// </summary>
    /// <param name="category">The disclosed category, or null for older peers and persisted entries.</param>
    /// <returns>The category icon, with a general item fallback.</returns>
    public static ObsidianIconKind GetIcon(string? category) => category switch
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
    /// Gets the localization resource key for a known category or the general fallback.
    /// </summary>
    /// <param name="category">The disclosed category.</param>
    /// <returns>The category label resource key.</returns>
    public static string GetLabelKey(string? category)
    {
        string key = category is _hpRecovery or _statusRecovery or _utility or _evolution or _ball or _tm or _battle or _held ? category : _utility;
        return _categoryKeyPrefix + key;
    }
}
