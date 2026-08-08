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
            "MALE" => ("♂", "male", "Male"),
            "FEMALE" => ("♀", "female", "Female"),
            _ => (null, null, null)
        };
    }
}
