using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Wardrobe;

/// <summary>
/// Selects native hue steps or an exact supported dye value without a continuous color picker.
/// </summary>
public partial class WardrobeDyePicker
{
    private const string _swatchPrefix = "--ow-swatch:";

    /// <summary>
    /// Gets or sets the localized dye label.
    /// </summary>
    [Parameter]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the native hue rotation from zero through 359 degrees.
    /// </summary>
    [Parameter]
    public int Value { get; set; }

    /// <summary>
    /// Gets or sets the engine-derived colors for the 36 ten-degree steps.
    /// </summary>
    [Parameter]
    public IReadOnlyList<string> Colors { get; set; } = [];

    /// <summary>
    /// Gets or sets optional base hair choices displayed below the shared compact heading.
    /// </summary>
    [Parameter]
    public RenderFragment? BaseColorOptions { get; set; }

    /// <summary>
    /// Gets or sets whether the game currently rejects editing.
    /// </summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>
    /// Gets or sets the callback for a chosen native hue.
    /// </summary>
    [Parameter]
    public EventCallback<int> Changed { get; set; }

    /// <summary>
    /// Creates the style for one native swatch.
    /// </summary>
    /// <param name="index">The zero-based ten-degree hue step.</param>
    /// <returns>The swatch color declaration.</returns>
    private string ColorStyle(int index)
        => _swatchPrefix + (index < Colors.Count ? Colors[index] : WardrobeUi.NeutralColor);

    /// <summary>
    /// Accepts only an integral hue value within the native range.
    /// </summary>
    /// <param name="args">The numeric input's changed value.</param>
    /// <returns>Completion after an accepted value is delivered.</returns>
    private Task ChangeExactAsync(ChangeEventArgs args)
        => int.TryParse(args.Value?.ToString(), out int value) && value is >= 0 and < 360 ? Changed.InvokeAsync(value) : Task.CompletedTask;
}
