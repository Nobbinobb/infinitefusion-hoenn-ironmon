using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Renders the Overview page of one authorized Pokemon inspection.
/// </summary>
public partial class DebugPokemonOverview
{
    /// <summary>
    /// Gets or sets the game-owned inspector snapshot.
    /// </summary>
    [Parameter]
    public DebugPokemonIdentitySnapshot Pokemon { get; set; } = null!;

    /// <summary>
    /// Formats the numeric form and optional localized name.
    /// </summary>
    /// <returns>The visible form text.</returns>
    private string GetFormText()
        => string.IsNullOrWhiteSpace(Pokemon.FormName) ? Pokemon.Form.ToString() : $"{Pokemon.Form} · {Pokemon.FormName}";

}
