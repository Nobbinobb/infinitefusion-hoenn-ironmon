using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

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
    /// Gets the first evolution requirement or the absence label.
    /// </summary>
    /// <returns>The evolution requirement.</returns>
    private string GetEvolutionRequirement()
        => Pokemon.Evolutions.Count == 0 ? Text["Lookup.Card.None"] : Pokemon.Evolutions[0].Label;

    /// <summary>
    /// Formats an authored encounter-table chance.
    /// </summary>
    /// <param name="chance">The percentage chance.</param>
    /// <param name="conditional">Whether the chance assumes a fusion event already triggered.</param>
    /// <returns>The compact percentage label.</returns>
    private string FormatChance(decimal chance, bool conditional)
        => conditional ? Text["Lookup.Card.ChanceWhenFused", chance] : $"{chance:0.##}%";

    /// <summary>
    /// Formats the authored slot or ordered slot pair for a wild occurrence.
    /// </summary>
    /// <param name="occurrence">The represented wild occurrence.</param>
    /// <returns>The compact slot label.</returns>
    private string FormatWildSlots(WildPokemonOccurrenceSnapshot occurrence)
        => occurrence.SecondarySlot is null ? Text["Lookup.Card.SlotNumber", occurrence.Slot] : Text["Lookup.Card.CombinedSlots", occurrence.Slot, occurrence.SecondarySlot];

    /// <summary>
    /// Formats one authored wild-encounter level or level range.
    /// </summary>
    /// <param name="minimumLevel">The minimum encounter level.</param>
    /// <param name="maximumLevel">The maximum encounter level.</param>
    /// <returns>The compact level label.</returns>
    private string FormatLevelRange(int minimumLevel, int maximumLevel)
        => minimumLevel == maximumLevel ? Text["Lookup.Card.LevelValue", minimumLevel] : Text["Lookup.Card.LevelRange", minimumLevel, maximumLevel];

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
