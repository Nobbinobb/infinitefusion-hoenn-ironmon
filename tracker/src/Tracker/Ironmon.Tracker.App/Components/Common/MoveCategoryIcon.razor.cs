using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Renders an accessible icon for a move damage category.
/// </summary>
public partial class MoveCategoryIcon
{
    /// <summary>
    /// Gets or sets the represented move category.
    /// </summary>
    [Parameter]
    public MoveCategory Category { get; set; }

    /// <summary>
    /// Gets the category-specific CSS classes.
    /// </summary>
    /// <returns>The icon CSS classes.</returns>
    private string GetCssClass() => Category switch
    {
        MoveCategory.Physical => "category-icon physical",
        MoveCategory.Special => "category-icon special",
        MoveCategory.Status => "category-icon status",
        _ => "category-icon unknown"
    };

    /// <summary>
    /// Gets the accessible category label.
    /// </summary>
    /// <returns>The localized-neutral category label.</returns>
    private string GetLabel() => Category switch
    {
        MoveCategory.Physical => Text["Common.Move.Physical"],
        MoveCategory.Special => Text["Common.Move.Special"],
        MoveCategory.Status => Text["Common.Move.Status"],
        _ => Text["Common.Move.UnknownCategory"]
    };
}
