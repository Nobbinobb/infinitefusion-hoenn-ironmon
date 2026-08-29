using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Renders one known player or enemy move using shared tracker presentation.
/// </summary>
public partial class MoveRow
{
    private const string UnknownPowerText = "???";

    /// <summary>
    /// Gets or sets the move name.
    /// </summary>
    [Parameter]
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the stable move type.
    /// </summary>
    [Parameter]
    public required string Type { get; set; }

    /// <summary>
    /// Gets or sets the move damage category.
    /// </summary>
    [Parameter]
    public MoveCategory Category { get; set; }

    /// <summary>
    /// Gets or sets visible type-only effectiveness when a target exists.
    /// </summary>
    [Parameter]
    public MoveEffectiveness? Effectiveness { get; set; }

    /// <summary>
    /// Gets or sets whether the move receives a same-type attack bonus.
    /// </summary>
    [Parameter]
    public bool Stab { get; set; }

    /// <summary>
    /// Gets or sets the formatted PP display.
    /// </summary>
    [Parameter]
    public required string PpText { get; set; }

    /// <summary>
    /// Gets or sets the known remaining PP, or null when it has not been observed.
    /// </summary>
    [Parameter]
    public int? CurrentPp { get; set; }

    /// <summary>
    /// Gets or sets the move's total PP.
    /// </summary>
    [Parameter]
    public int TotalPp { get; set; }

    /// <summary>
    /// Gets or sets base move power.
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
    /// Gets or sets the callback raised when the move is selected.
    /// </summary>
    [Parameter]
    public EventCallback Selected { get; set; }

    /// <summary>
    /// Formats the move power for its labeled column.
    /// </summary>
    /// <returns>The move-power value.</returns>
    private string FormatPower()
        => PowerPresentation?.Display ?? (Power == MoveDataConstants.NoBasePower ? "—" : Power.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Determines whether the compact leading power marker should be visible.
    /// </summary>
    /// <returns>True when a known value has a semantic power marker.</returns>
    private bool ShouldShowPowerIndicator()
    {
        return PowerPresentation is not null
            && PowerPresentation.Indicator != MovePowerIndicator.None
            && !string.Equals(PowerPresentation.Display, UnknownPowerText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets localized accessible text for the move-power semantic indicator.
    /// </summary>
    /// <returns>The indicator description.</returns>
    private string GetPowerIndicatorTitle() => PowerPresentation?.Indicator switch
    {
        MovePowerIndicator.Conditional => Text["Common.Move.ConditionalPower"],
        MovePowerIndicator.MultiHit => Text["Common.Move.MultiHitPower"],
        MovePowerIndicator.FixedDamage => Text["Common.Move.FixedDamage"],
        _ => string.Empty
    };

    /// <summary>
    /// Formats move accuracy for its labeled column.
    /// </summary>
    /// <returns>The move-accuracy value.</returns>
    private string FormatAccuracy()
    {
        return Accuracy == MoveDataConstants.AlwaysHitsAccuracy
            ? "—"
            : MovePresentation.CalculateAdjustedAccuracy(Accuracy, AccuracyStage, TargetEvasionStage).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets the visual class for power based on visible type effectiveness.
    /// </summary>
    /// <returns>The move-power CSS classes.</returns>
    private string GetPowerClass() => Effectiveness switch
    {
        MoveEffectiveness.Double or MoveEffectiveness.Quadruple => "move-power increased",
        MoveEffectiveness.Half or MoveEffectiveness.Quarter or MoveEffectiveness.Immune => "move-power decreased",
        _ => "move-power"
    };

    /// <summary>
    /// Gets the visual class for the remaining PP threshold.
    /// </summary>
    /// <returns>The move-PP CSS classes.</returns>
    private string GetPpClass()
    {
        if (CurrentPp is null || TotalPp <= 0)
            return "move-pp";

        if (CurrentPp.Value * 100 < TotalPp * 30)
            return "move-pp critical";

        return CurrentPp.Value * 100 < TotalPp * 60 ? "move-pp low" : "move-pp";
    }

    /// <summary>
    /// Gets the current-and-maximum PP tooltip for the compact value.
    /// </summary>
    /// <returns>The current and maximum PP display.</returns>
    private string GetPpTitle()
        => $"{CurrentPp?.ToString(CultureInfo.InvariantCulture) ?? "--"} / {TotalPp.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Gets the visual class for an accuracy-stage adjustment.
    /// </summary>
    /// <returns>The move-accuracy CSS classes.</returns>
    private string GetAccuracyClass()
    {
        if (Accuracy == MoveDataConstants.AlwaysHitsAccuracy)
            return "move-accuracy";

        int effectiveStage = Math.Clamp(AccuracyStage - TargetEvasionStage, -6, 6);
        return effectiveStage switch
        {
            > 0 => "move-accuracy increased",
            < 0 => "move-accuracy decreased",
            _ => "move-accuracy"
        };
    }

    /// <summary>
    /// Gets detail explaining the displayed accuracy adjustment.
    /// </summary>
    /// <returns>The base accuracy and relevant battle stages.</returns>
    private string GetAccuracyTitle()
    {
        if (Accuracy == MoveDataConstants.AlwaysHitsAccuracy)
            return Text["Common.Move.Always"];

        string accuracyStage = BattleStatStageFormatter.FormatSignedStage(AccuracyStage);
        string evasionStage = BattleStatStageFormatter.FormatSignedStage(TargetEvasionStage);
        return Text["Common.Move.AdjustedAccuracy", Accuracy, string.IsNullOrEmpty(accuracyStage) ? "0" : accuracyStage, string.IsNullOrEmpty(evasionStage) ? "0" : evasionStage];
    }

    /// <summary>
    /// Gets localized accessible detail for an effectiveness symbol.
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
