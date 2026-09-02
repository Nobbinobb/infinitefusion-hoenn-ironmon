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
                ("ATK", "--", StatAdjustment.Neutral), ("SPA", "--", StatAdjustment.Neutral),
                ("DEF", "--", StatAdjustment.Neutral), ("SPD", "--", StatAdjustment.Neutral)];
        }

        return [("SPE", Format(Player.Speed), Player.NatureAdjustments.Speed), ("HP", Format(Player.MaximumHp), StatAdjustment.Neutral),
            ("ATK", Format(Player.Attack), Player.NatureAdjustments.Attack), ("SPA", Format(Player.SpecialAttack), Player.NatureAdjustments.SpecialAttack),
            ("DEF", Format(Player.Defense), Player.NatureAdjustments.Defense), ("SPD", Format(Player.SpecialDefense), Player.NatureAdjustments.SpecialDefense)];
    }

    /// <summary>
    /// Formats one stat value without culture-dependent separators.
    /// </summary>
    /// <param name="value">The stat value.</param>
    /// <returns>The formatted stat value.</returns>
    private static string Format(int value)
        => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the CSS classes for a stat value and its nature adjustment.
    /// </summary>
    /// <param name="adjustment">The nature adjustment.</param>
    /// <returns>The stat-value CSS classes.</returns>
    private static string GetStatValueClass(StatAdjustment adjustment) => adjustment switch
    {
        StatAdjustment.Increased => "stat-value increased",
        StatAdjustment.Decreased => "stat-value decreased",
        _ => "stat-value"
    };
}
