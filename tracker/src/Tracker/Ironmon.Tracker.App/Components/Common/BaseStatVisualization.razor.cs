using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Renders base-stat table and bar presentations with an optional individual-values view.
/// </summary>
public partial class BaseStatVisualization
{
    private BaseStatDisplayMode _mode = BaseStatDisplayMode.Table;

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
    /// Gets or sets raw IVs for the optional individual-values presentation.
    /// </summary>
    [Parameter]
    public IndividualValuesSnapshot? Ivs { get; set; }

    /// <summary>
    /// Gets or sets effective IVs, including Hyper Training when recorded.
    /// </summary>
    [Parameter]
    public IndividualValuesSnapshot? EffectiveIvs { get; set; }

    /// <summary>
    /// Gets or sets optional IV bars and an owned-individual selector for archived runs.
    /// </summary>
    [Parameter]
    public RenderFragment? IndividualValuesContent { get; set; }

    /// <summary>
    /// Gets whether the selected Pokemon has any usable individual values.
    /// </summary>
    private bool HasIndividualValues => PokemonIndividualValues.HasKnownValues(Ivs) || PokemonIndividualValues.HasKnownValues(EffectiveIvs);

    /// <summary>
    /// Returns to the table when a newly selected Pokemon has no IV data.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (_mode == BaseStatDisplayMode.IndividualValues && !HasIndividualValues)
            _mode = BaseStatDisplayMode.Table;
    }

    /// <summary>
    /// Selects the visible base-stat presentation.
    /// </summary>
    /// <param name="mode">The requested presentation.</param>
    private void SelectMode(BaseStatDisplayMode mode)
    {
        if (mode != BaseStatDisplayMode.IndividualValues || HasIndividualValues)
            _mode = mode;
    }

    /// <summary>
    /// Gets all final base stats in game display order.
    /// </summary>
    /// <returns>The original, generated, and optional dominance values.</returns>
    private IReadOnlyList<(string Name, int Original, int Generated, string? Dominance)> GetBaseStats()
    {
        string? head = Fusion ? Text["Common.BaseStats.Head"].Value : null;
        string? body = Fusion ? Text["Common.BaseStats.Body"].Value : null;
        return
        [
            ("HP", Original.Hp, Generated.Hp, head),
            ("SPA", Original.SpecialAttack, Generated.SpecialAttack, head),
            ("SPD", Original.SpecialDefense, Generated.SpecialDefense, head),
            ("ATK", Original.Attack, Generated.Attack, body),
            ("DEF", Original.Defense, Generated.Defense, body),
            ("SPE", Original.Speed, Generated.Speed, body)
        ];
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
