using Ironmon.Tracker.Protocol;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components;

/// <summary>
/// Renders complete deterministic information returned by a post-run lookup.
/// </summary>
public partial class PokemonLookupCard
{
    private string? _spriteKey;
    private string? _spriteSource;
    private AbilitySnapshot? _selectedAbility;

    /// <summary>
    /// Gets or sets the reconstructed Pokemon information.
    /// </summary>
    [Parameter]
    public PokemonLookupSnapshot Pokemon { get; set; } = null!;

    /// <summary>
    /// Gets or sets the completed-run reconstruction recipe.
    /// </summary>
    [Parameter]
    public CompletedRunRecipePayload? Recipe { get; set; }

    /// <summary>
    /// Gets or sets whether the card represents an authorized active-run lookup.
    /// </summary>
    [Parameter]
    public bool DebugMode { get; set; }

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a related Pokemon is selected.
    /// </summary>
    [Parameter]
    public EventCallback<string> PokemonSelected { get; set; }

    /// <summary>
    /// Refreshes the local sprite when the lookup result changes.
    /// </summary>
    protected override void OnParametersSet()
    {
        string? key = GameRoot is null || Pokemon.SpritePath is null ? null : $"{GameRoot}|{Pokemon.SpritePath}";
        if (key == _spriteKey)
            return;

        _spriteKey = key;
        _spriteSource = LocalSpriteLoader.Load(GameRoot, Pokemon.SpritePath);
    }

    /// <summary>
    /// Gets all base stats in tracker display order.
    /// </summary>
    /// <returns>The labeled base stats.</returns>
    private IReadOnlyList<(string Name, int Value)> GetBaseStats()
    {
        return [("SPE", Pokemon.BaseStats.Speed), ("HP", Pokemon.BaseStats.Hp),
         ("ATK", Pokemon.BaseStats.Attack), ("DEF", Pokemon.BaseStats.Defense),
         ("SPA", Pokemon.BaseStats.SpecialAttack), ("SPD", Pokemon.BaseStats.SpecialDefense)];
    }

    /// <summary>
    /// Gets the first evolution requirement or the absence label.
    /// </summary>
    /// <returns>The evolution requirement.</returns>
    private string GetEvolutionRequirement() 
        => Pokemon.Evolutions.Count == 0 ? "None" : Pokemon.Evolutions[0].Label;

    /// <summary>
    /// Opens full information for one generated ability.
    /// </summary>
    /// <param name="ability">The selected ability.</param>
    private void SelectAbility(AbilitySnapshot ability) 
        => _selectedAbility = ability;

    /// <summary>
    /// Closes the generated ability information.
    /// </summary>
    private void CloseAbility() 
        => _selectedAbility = null;
}
