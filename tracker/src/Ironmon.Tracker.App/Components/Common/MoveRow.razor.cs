using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Renders one known player or enemy move using shared tracker presentation.
/// </summary>
public partial class MoveRow
{
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
    /// Gets or sets the formatted PP display.
    /// </summary>
    [Parameter]
    public required string PpText { get; set; }

    /// <summary>
    /// Gets or sets base move power.
    /// </summary>
    [Parameter]
    public int Power { get; set; }

    /// <summary>
    /// Gets or sets move accuracy.
    /// </summary>
    [Parameter]
    public int Accuracy { get; set; }

    /// <summary>
    /// Gets or sets the callback raised when the move is selected.
    /// </summary>
    [Parameter]
    public EventCallback Selected { get; set; }

    /// <summary>
    /// Formats the move power with its localized compact label.
    /// </summary>
    /// <returns>The compact move-power display.</returns>
    private string FormatPower()
        => Power == MoveDataConstants.NoBasePower ? Text["Common.Move.NoPowerCompact"] : Text["Common.Move.PowerCompact", Power];

    /// <summary>
    /// Formats move accuracy with its localized compact label.
    /// </summary>
    /// <returns>The compact accuracy display.</returns>
    private string FormatAccuracy()
        => Accuracy == MoveDataConstants.AlwaysHitsAccuracy ? Text["Common.Move.AlwaysAccuracyCompact"] : Text["Common.Move.AccuracyCompact", Accuracy.ToString(CultureInfo.InvariantCulture)];

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
