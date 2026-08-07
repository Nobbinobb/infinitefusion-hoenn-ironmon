using Ironmon.Tracker.Core;
using Ironmon.Tracker.Protocol;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components;

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
}
