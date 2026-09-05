using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Renders one consistent compact run-obtainability indicator.
/// </summary>
public partial class PokemonObtainabilityMark
{
    private const string _calculatingIcon = "…";
    private const string _obtainableIcon = "✓";
    private const string _unobtainableIcon = "×";

    /// <summary>
    /// Gets or sets the available run-obtainability state.
    /// </summary>
    [Parameter]
    public PokemonObtainabilityStatus? Status { get; set; }

    /// <summary>
    /// Gets or sets whether the indicator uses a text label instead of a glyph.
    /// </summary>
    [Parameter]
    public bool ShowLabel { get; set; }

    /// <summary>
    /// Gets or sets whether a visible status label also includes its compact status icon.
    /// </summary>
    [Parameter]
    public bool IncludeIcon { get; set; }

    /// <summary>
    /// Gets the three-state UI value, using calculating until a status is available.
    /// </summary>
    private PokemonObtainabilityStatus DisplayStatus => Status switch
    {
        PokemonObtainabilityStatus.Obtainable => PokemonObtainabilityStatus.Obtainable,
        PokemonObtainabilityStatus.Unobtainable => PokemonObtainabilityStatus.Unobtainable,
        _ => PokemonObtainabilityStatus.Calculating
    };

    /// <summary>
    /// Gets the localized accessible status label.
    /// </summary>
    private string Label => DisplayStatus switch
    {
        PokemonObtainabilityStatus.Obtainable => Text["Lookup.Obtainability.Obtainable"],
        PokemonObtainabilityStatus.Unobtainable => Text["Lookup.Obtainability.Unobtainable"],
        _ => Text["Lookup.Obtainability.Calculating"]
    };

    /// <summary>
    /// Gets the compact status glyph.
    /// </summary>
    private string Icon => DisplayStatus switch
    {
        PokemonObtainabilityStatus.Obtainable => _obtainableIcon,
        PokemonObtainabilityStatus.Unobtainable => _unobtainableIcon,
        _ => _calculatingIcon
    };

    /// <summary>
    /// Gets the visual classes for the requested presentation.
    /// </summary>
    private string CssClass => ShowLabel
        ? $"obtainability-state {DisplayStatus.ToString().ToLowerInvariant()}"
        : $"pokemon-obtainability-mark {DisplayStatus.ToString().ToLowerInvariant()}";
}
