using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents trainer progress and disclosed party information in the redesigned lookup.
/// </summary>
public partial class ObsidianLookupTrainer
{
    private int? _selectedSlot;
    private string? _description;
    private string? _observedEntryId;
    private bool _observedAbilityAccess;
    private bool _observedMoveAccess;
    private AreaTrainerEntryPayload? _observedTrainer;

    /// <summary>
    /// Gets or sets the trainer entry with its disclosure state.
    /// </summary>
    [Parameter, EditorRequired]
    public AreaTrainerEntryPayload Trainer { get; set; } = null!;

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets whether the current source permits ability information.
    /// </summary>
    [Parameter]
    public bool CanViewAbilities { get; set; }

    /// <summary>
    /// Gets or sets whether the current source permits move information.
    /// </summary>
    [Parameter]
    public bool CanViewMoves { get; set; }

    /// <summary>
    /// Gets or sets whether the existing Pokemon lookup is authorized.
    /// </summary>
    [Parameter]
    public bool CanLookupPokemon { get; set; }

    /// <summary>
    /// Gets or sets navigation to the existing generated Pokemon lookup.
    /// </summary>
    [Parameter]
    public EventCallback<string> PokemonSelected { get; set; }

    /// <summary>
    /// Clears selection when another trainer occupies the rendered entry.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (_observedEntryId != Trainer.EntryId || !Trainer.DetailsRevealed || _observedAbilityAccess != CanViewAbilities || _observedMoveAccess != CanViewMoves)
        {
            _selectedSlot = null;
            _description = null;
        }

        _observedEntryId = Trainer.EntryId;
        if (!ReferenceEquals(_observedTrainer, Trainer))
            _description = null;

        _observedTrainer = Trainer;
        _observedAbilityAccess = CanViewAbilities;
        _observedMoveAccess = CanViewMoves;
    }

    /// <summary>
    /// Expands one battle set and clears the previously selected effect.
    /// </summary>
    /// <param name="slot">The selected one-based party slot.</param>
    private void TogglePokemon(int slot)
    {
        _selectedSlot = _selectedSlot == slot ? null : slot;
        _description = null;
    }

    /// <summary>
    /// Displays only the selected move or ability's effect text.
    /// </summary>
    /// <param name="description">The localized effect description.</param>
    private void SelectDescription(string description)
        => _description = description;

    /// <summary>
    /// Opens a disclosed party member only when lookup access is available.
    /// </summary>
    /// <param name="pokemon">The member selected by the user.</param>
    /// <returns>The parent navigation callback.</returns>
    private Task OpenPokemonAsync(AreaTrainerPokemonPayload pokemon)
        => CanLookupPokemon && Trainer.DetailsRevealed && Trainer.Party.Contains(pokemon) ? PokemonSelected.InvokeAsync(pokemon.SpeciesId) : Task.CompletedTask;
}
