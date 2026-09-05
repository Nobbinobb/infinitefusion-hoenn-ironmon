using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Renders the Overview page of one authorized Pokemon inspection.
/// </summary>
public partial class DebugPokemonOverview
{
    /// <summary>
    /// Gets or sets whether the parent presents the redesigned identity header.
    /// </summary>
    [Parameter]
    public bool Redesigned { get; set; }

    /// <summary>
    /// Gets or sets the game-owned inspector snapshot.
    /// </summary>
    [Parameter]
    public DebugPokemonIdentitySnapshot Pokemon { get; set; } = null!;

    /// <summary>
    /// Gets or sets the generated typing from the shared lookup snapshot.
    /// </summary>
    [Parameter]
    public IReadOnlyList<string> Types { get; set; } = [];

    /// <summary>
    /// Gets or sets the run-obtainability state shown beside the inspected identity.
    /// </summary>
    [Parameter]
    public PokemonObtainabilityStatus? ObtainabilityStatus { get; set; }

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Formats the numeric form and optional localized name.
    /// </summary>
    /// <returns>The visible form text.</returns>
    private string GetFormText()
        => string.IsNullOrWhiteSpace(Pokemon.FormName) ? Pokemon.Form.ToString() : $"{Pokemon.Form} · {Pokemon.FormName}";

    /// <summary>
    /// Gets whether the species name adds information beyond the visible nickname.
    /// </summary>
    /// <returns>Whether to show the species as secondary identity metadata.</returns>
    private bool HasDistinctNickname()
        => !string.Equals(Pokemon.Nickname, Pokemon.SpeciesName, StringComparison.OrdinalIgnoreCase);
}
