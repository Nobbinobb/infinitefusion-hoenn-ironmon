using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Renders table and bar presentations for original and generated base stats.
/// </summary>
public partial class BaseStatVisualization
{
    private BaseStatDisplayMode _mode = BaseStatDisplayMode.Table;

    /// <summary>
    /// Gets or sets whether this content uses the redesigned research presentation.
    /// </summary>
    [CascadingParameter(Name = nameof(PokemonLookupCard.ResearchRedesigned))]
    public bool Redesigned { get; set; }

    /// <summary>
    /// Gets or sets the original final base stats.
    /// </summary>
    [Parameter]
    public BaseStatsSnapshot Original { get; set; } = new();

    /// <summary>
    /// Gets or sets the generated final base stats.
    /// </summary>
    [Parameter]
    public BaseStatsSnapshot Generated { get; set; } = new();

    /// <summary>
    /// Gets or sets the original final base-stat total.
    /// </summary>
    [Parameter]
    public int OriginalTotal { get; set; }

    /// <summary>
    /// Gets or sets the generated final base-stat total.
    /// </summary>
    [Parameter]
    public int GeneratedTotal { get; set; }

    /// <summary>
    /// Gets or sets whether the represented run used generated base stats.
    /// </summary>
    [Parameter]
    public bool Randomized { get; set; } = true;

    /// <summary>
    /// Gets or sets whether fusion dominance labels should be shown.
    /// </summary>
    [Parameter]
    public bool Fusion { get; set; }

    /// <summary>
    /// Selects the visible base-stat presentation.
    /// </summary>
    /// <param name="mode">The requested presentation.</param>
    private void SelectMode(BaseStatDisplayMode mode)
        => _mode = mode;

    /// <summary>
    /// Gets all final base stats in game display order.
    /// </summary>
    /// <returns>The original, generated, and optional dominance values.</returns>
    private IReadOnlyList<(string Name, int Original, int Generated, string? Dominance)> GetBaseStats()
    {
        string? head = Fusion ? Text["Common.BaseStats.Head"].Value : null;
        string? body = Fusion ? Text["Common.BaseStats.Body"].Value : null;
        (string Name, int Original, int Generated, string? Dominance)[] stats =
        [
            ("HP", Original.Hp, Generated.Hp, head),
            ("ATK", Original.Attack, Generated.Attack, body),
            ("DEF", Original.Defense, Generated.Defense, body),
            ("SPA", Original.SpecialAttack, Generated.SpecialAttack, head),
            ("SPD", Original.SpecialDefense, Generated.SpecialDefense, head),
            ("SPE", Original.Speed, Generated.Speed, body)
        ];

        return Redesigned ? [stats[0], stats[3], stats[4], stats[1], stats[2], stats[5]] : stats;
    }

    /// <summary>
    /// Formats the signed difference between generated and original values.
    /// </summary>
    /// <param name="original">The original value.</param>
    /// <param name="generated">The generated value.</param>
    /// <returns>The signed delta.</returns>
    private static string FormatDelta(int original, int generated)
    {
        int delta = generated - original;
        return delta > 0 ? $"+{delta}" : delta.ToString();
    }

    /// <summary>
    /// Gets the presentation class for a generated-stat difference.
    /// </summary>
    /// <param name="original">The original value.</param>
    /// <param name="generated">The generated value.</param>
    /// <returns>The positive, negative, or unchanged class.</returns>
    private static string GetDeltaClass(int original, int generated)
        => generated.CompareTo(original) switch
        {
            > 0 => TrackerUiConstants.PositiveCssClass,
            < 0 => TrackerUiConstants.NegativeCssClass,
            _ => TrackerUiConstants.UnchangedCssClass
        };

    /// <summary>
    /// Converts a base-stat value to a bounded percentage of the normal 255 maximum.
    /// </summary>
    /// <param name="value">The base-stat value.</param>
    /// <returns>The percentage bar width.</returns>
    private static string GetBarWidth(int value)
        => Math.Clamp(value / (decimal)TrackerUiConstants.MaximumBaseStat * TrackerUiConstants.FullPercentage, decimal.Zero, TrackerUiConstants.FullPercentage).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the shared original/generated portion of a delta bar.
    /// </summary>
    /// <param name="original">The original base-stat value.</param>
    /// <param name="generated">The generated base-stat value.</param>
    /// <returns>The shared portion as a percentage of 255.</returns>
    private static string GetSharedBarWidth(int original, int generated)
        => GetBarWidth(Math.Min(original, generated));

    /// <summary>
    /// Gets the changed portion of a delta bar.
    /// </summary>
    /// <param name="original">The original base-stat value.</param>
    /// <param name="generated">The generated base-stat value.</param>
    /// <returns>The absolute difference as a percentage of 255.</returns>
    private static string GetDeltaBarWidth(int original, int generated)
        => GetBarWidth(Math.Abs(generated - original));

    /// <summary>
    /// Gets the selected class for one display-mode button.
    /// </summary>
    /// <param name="mode">The represented display mode.</param>
    /// <returns>The button CSS classes.</returns>
    private string GetModeClass(BaseStatDisplayMode mode)
        => mode == _mode ? TrackerUiConstants.SelectedCssClass : string.Empty;
}
