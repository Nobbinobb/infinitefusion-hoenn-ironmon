using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Renders a symbol for a male or female Pokemon and omits genderless Pokemon.
/// </summary>
public partial class GenderIcon
{
    private string? _symbol;
    private string? _cssClass;
    private string? _label;

    /// <summary>
    /// Gets or sets the stable gender identifier.
    /// </summary>
    [Parameter]
    public string? Gender { get; set; }

    /// <summary>
    /// Resolves the visible symbol and accessibility metadata.
    /// </summary>
    protected override void OnParametersSet()
    {
        (_symbol, _cssClass, _label) = Gender?.ToUpperInvariant() switch
        {
            PokemonValueIds.Male => (PokemonValueIds.MaleSymbol, PokemonValueIds.MaleCssClass, Text["Common.Gender.Male"].Value),
            PokemonValueIds.Female => (PokemonValueIds.FemaleSymbol, PokemonValueIds.FemaleCssClass, Text["Common.Gender.Female"].Value),
            _ => (null, null, null)
        };
    }
}
