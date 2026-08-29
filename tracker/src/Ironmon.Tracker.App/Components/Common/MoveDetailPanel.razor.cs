using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Displays all legally known information for a selected move.
/// </summary>
public partial class MoveDetailPanel
{
    private const string DashText = "—";
    private const string ZeroPowerText = "0";
    private const string ChanceNumberFormat = "0.##";

    /// <summary>
    /// Gets or sets the localized move name.
    /// </summary>
    [Parameter]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the stable move type identifier.
    /// </summary>
    [Parameter]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the move damage category.
    /// </summary>
    [Parameter]
    public MoveCategory Category { get; set; }

    /// <summary>
    /// Gets or sets the localized move description.
    /// </summary>
    [Parameter]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base move power.
    /// </summary>
    [Parameter]
    public int Power { get; set; }

    /// <summary>
    /// Gets or sets the calculated display for conditional or nonstandard power.
    /// </summary>
    [Parameter]
    public MovePowerPresentationSnapshot? PowerPresentation { get; set; }

    /// <summary>
    /// Gets or sets move accuracy.
    /// </summary>
    [Parameter]
    public int Accuracy { get; set; }

    /// <summary>
    /// Gets or sets the move user's current accuracy stage.
    /// </summary>
    [Parameter]
    public int AccuracyStage { get; set; }

    /// <summary>
    /// Gets or sets the target's current evasion stage.
    /// </summary>
    [Parameter]
    public int TargetEvasionStage { get; set; }

    /// <summary>
    /// Gets or sets the formatted PP information.
    /// </summary>
    [Parameter]
    public string PpText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the known learn level when applicable.
    /// </summary>
    [Parameter]
    public int? LearnedLevel { get; set; }

    /// <summary>
    /// Gets or sets visible type-only effectiveness when a target exists.
    /// </summary>
    [Parameter]
    public MoveEffectiveness? Effectiveness { get; set; }

    /// <summary>
    /// Gets or sets the callback raised when the panel should close.
    /// </summary>
    [Parameter]
    public EventCallback Closed { get; set; }

    /// <summary>
    /// Closes the move-information panel.
    /// </summary>
    /// <returns>A task representing callback delivery.</returns>
    private Task Close()
        => Closed.InvokeAsync();

    /// <summary>
    /// Gets the localized-neutral move-category name.
    /// </summary>
    /// <returns>The move-category name.</returns>
    private string GetCategoryName() => Category switch
    {
        MoveCategory.Physical => Text["Common.Move.Physical"],
        MoveCategory.Special => Text["Common.Move.Special"],
        MoveCategory.Status => Text["Common.Move.Status"],
        _ => Text["Common.Move.Unknown"]
    };

    /// <summary>
    /// Gets move power for display.
    /// </summary>
    /// <returns>The move power or a dash for status moves.</returns>
    private string GetPowerText()
        => PowerPresentation?.Display ?? (Power == 0 ? "—" : Power.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Builds the structured move-power rows for the current presentation.
    /// </summary>
    /// <returns>The formatted outcome, cumulative value, and probability rows.</returns>
    private IReadOnlyList<(string Outcome, string Value, string Chance)> GetPowerDetailRows()
    {
        if (PowerPresentation is null)
            return [];

        if (PowerPresentation.DetailsKind == MovePowerDetailsKind.TripleKick)
            return GetTripleKickDetailRows();

        return [.. PowerPresentation.Outcomes.Select(FormatPowerOutcome)];
    }

    /// <summary>
    /// Calculates sequential Triple Kick outcomes from the currently displayed accuracy.
    /// </summary>
    /// <returns>The miss and cumulative-hit outcome rows.</returns>
    private IReadOnlyList<(string Outcome, string Value, string Chance)> GetTripleKickDetailRows()
    {
        if (PowerPresentation is null || PowerPresentation.Outcomes.Count < 3)
            return [];

        decimal hitChance = Accuracy == MoveDataConstants.AlwaysHitsAccuracy
            ? 1m
            : MovePresentation.CalculateAdjustedAccuracy(Accuracy, AccuracyStage, TargetEvasionStage) / 100m;

        decimal missChance = 1m - hitChance;
        List<(string Outcome, string Value, string Chance)> rows = [(Text["Common.Move.MissOutcome"], ZeroPowerText, FormatChance(missChance * 100m))];
        if (!PowerPresentation.AccuracyCheckedPerHit)
        {
            MovePowerOutcomeSnapshot finalOutcome = PowerPresentation.Outcomes[2];
            rows.Add((Text["Common.Move.HitsOutcome", 3], FormatPowerValue(finalOutcome.Power), FormatChance(hitChance * 100m)));
            return rows;
        }

        decimal firstOnly = hitChance * missChance;
        decimal firstTwo = hitChance * hitChance * missChance;
        decimal allThree = hitChance * hitChance * hitChance;
        rows.Add((Text["Common.Move.HitsOutcome", 1], FormatPowerValue(PowerPresentation.Outcomes[0].Power), FormatChance(firstOnly * 100m)));
        rows.Add((Text["Common.Move.HitsOutcome", 2], FormatPowerValue(PowerPresentation.Outcomes[1].Power), FormatChance(firstTwo * 100m)));
        rows.Add((Text["Common.Move.HitsOutcome", 3], FormatPowerValue(PowerPresentation.Outcomes[2].Power), FormatChance(allThree * 100m)));
        return rows;
    }

    /// <summary>
    /// Formats one tracker-provided move-power outcome.
    /// </summary>
    /// <param name="outcome">The outcome to format.</param>
    /// <returns>The formatted table row.</returns>
    private (string Outcome, string Value, string Chance) FormatPowerOutcome(MovePowerOutcomeSnapshot outcome)
    {
        string chance = outcome.ChancePercent is null ? DashText : FormatChance(outcome.ChancePercent.Value);
        return outcome.Kind switch
        {
            MovePowerOutcomeKind.Healing => (Text["Common.Move.HealTarget"], Text["Common.Move.TargetHpPercent", outcome.HealingPercent ?? 0], chance),
            MovePowerOutcomeKind.Range => (Text["Common.Move.DamageRange"], Text["Common.Move.HpRange", outcome.Minimum ?? 0, outcome.Maximum ?? 0], chance),
            MovePowerOutcomeKind.Hits => (Text["Common.Move.HitsOutcome", outcome.Hits ?? 0], FormatPowerValue(outcome.Power), chance),
            _ => (Text["Common.Move.PowerOutcome"], FormatPowerValue(outcome.Power), chance)
        };
    }

    /// <summary>
    /// Formats a nullable cumulative power value.
    /// </summary>
    /// <param name="power">The cumulative power.</param>
    /// <returns>The power text.</returns>
    private static string FormatPowerValue(int? power)
        => power?.ToString(CultureInfo.InvariantCulture) ?? DashText;

    /// <summary>
    /// Formats an outcome percentage without unnecessary trailing zeroes.
    /// </summary>
    /// <param name="chance">The percentage value.</param>
    /// <returns>The formatted percentage.</returns>
    private static string FormatChance(decimal chance)
        => $"{chance.ToString(ChanceNumberFormat, CultureInfo.InvariantCulture)}%";

    /// <summary>
    /// Gets move accuracy for display.
    /// </summary>
    /// <returns>The accuracy percentage or Always.</returns>
    private string GetAccuracyText()
        => Accuracy == 0 ? Text["Common.Move.Always"] : $"{Accuracy.ToString(CultureInfo.InvariantCulture)}%";

    /// <summary>
    /// Gets detail-panel CSS classes for visible move effectiveness.
    /// </summary>
    /// <param name="effectiveness">The calculated effectiveness.</param>
    /// <returns>The detail-effectiveness CSS classes.</returns>
    private static string GetDetailEffectivenessClass(MoveEffectiveness effectiveness) => effectiveness switch
    {
        MoveEffectiveness.Double or MoveEffectiveness.Quadruple => "detail-effectiveness increased",
        MoveEffectiveness.Half or MoveEffectiveness.Quarter => "detail-effectiveness decreased",
        MoveEffectiveness.Immune => "detail-effectiveness immune",
        _ => "detail-effectiveness"
    };

    /// <summary>
    /// Gets a non-empty move description.
    /// </summary>
    /// <returns>The localized description or fallback.</returns>
    private string GetDescription()
        => string.IsNullOrWhiteSpace(Description) ? Text["Common.Move.NoDescriptionAvailable"] : Description;

    /// <summary>
    /// Gets localized detail for visible move effectiveness.
    /// </summary>
    /// <param name="effectiveness">The calculated effectiveness.</param>
    /// <returns>The effectiveness description.</returns>
    private string GetEffectivenessTitle(MoveEffectiveness effectiveness) => effectiveness switch
    {
        MoveEffectiveness.Double => Text["Common.Move.SuperEffectiveDouble"],
        MoveEffectiveness.Quadruple => Text["Common.Move.SuperEffectiveQuadruple"],
        MoveEffectiveness.Half => Text["Common.Move.NotVeryEffectiveHalf"],
        MoveEffectiveness.Quarter => Text["Common.Move.NotVeryEffectiveQuarter"],
        MoveEffectiveness.Immune => Text["Common.Move.NoEffect"],
        _ => Text["Common.Move.NormallyEffective"]
    };
}
