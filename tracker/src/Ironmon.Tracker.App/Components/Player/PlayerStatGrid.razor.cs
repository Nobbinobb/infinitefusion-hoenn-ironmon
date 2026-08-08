using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Player;

/// <summary>
/// Renders the player's calculated stats and visible nature adjustments.
/// </summary>
public partial class PlayerStatGrid
{
    /// <summary>
    /// Gets or sets the active player Pokemon.
    /// </summary>
    [Parameter]
    public PlayerPokemonSnapshot? Player { get; set; }

    /// <summary>
    /// Gets the calculated stats in tracker display order.
    /// </summary>
    /// <returns>The labeled stat values and nature adjustments.</returns>
    private IReadOnlyList<(string Name, string Value, StatAdjustment Adjustment)> GetStats()
    {
        if (Player is null)
        {
            return [("SPE", "--", StatAdjustment.Neutral), ("HP", "--", StatAdjustment.Neutral),
                ("ATK", "--", StatAdjustment.Neutral), ("DEF", "--", StatAdjustment.Neutral),
                ("SPA", "--", StatAdjustment.Neutral), ("SPD", "--", StatAdjustment.Neutral)];
        }

        return [("SPE", Format(Player.Speed), Player.NatureAdjustments.Speed), ("HP", Format(Player.MaximumHp), StatAdjustment.Neutral),
            ("ATK", Format(Player.Attack), Player.NatureAdjustments.Attack), ("DEF", Format(Player.Defense), Player.NatureAdjustments.Defense),
            ("SPA", Format(Player.SpecialAttack), Player.NatureAdjustments.SpecialAttack), ("SPD", Format(Player.SpecialDefense), Player.NatureAdjustments.SpecialDefense)];
    }

    /// <summary>
    /// Formats one stat value without culture-dependent separators.
    /// </summary>
    /// <param name="value">The stat value.</param>
    /// <returns>The formatted stat value.</returns>
    private static string Format(int value)
        => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the visible nature-adjustment arrow.
    /// </summary>
    /// <param name="adjustment">The nature adjustment.</param>
    /// <returns>The adjustment arrow.</returns>
    private static string GetNatureIndicator(StatAdjustment adjustment) => adjustment switch
    {
        StatAdjustment.Increased => "↑",
        StatAdjustment.Decreased => "↓",
        _ => string.Empty
    };

    /// <summary>
    /// Gets the CSS classes for a nature-adjustment arrow.
    /// </summary>
    /// <param name="adjustment">The nature adjustment.</param>
    /// <returns>The adjustment CSS classes.</returns>
    private static string GetNatureClass(StatAdjustment adjustment) => adjustment switch
    {
        StatAdjustment.Increased => "nature-adjustment increased",
        StatAdjustment.Decreased => "nature-adjustment decreased",
        _ => "nature-adjustment"
    };
}
