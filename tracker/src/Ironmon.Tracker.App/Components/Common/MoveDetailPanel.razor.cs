using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Displays all legally known information for a selected move.
/// </summary>
public partial class MoveDetailPanel
{
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
    /// Gets or sets move accuracy.
    /// </summary>
    [Parameter]
    public int Accuracy { get; set; }

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
        => Power == 0 ? "—" : Power.ToString(CultureInfo.InvariantCulture);

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
