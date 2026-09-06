using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Enemy;

/// <summary>
/// Renders and coordinates the selected opposing-Pokemon tracker card.
/// </summary>
public partial class EnemyCard : IDisposable
{
    private bool _abilitiesOpen;
    private bool _defenseOpen;
    private string? _defensePokemonId;
    private string? _dialogBattleId;

    /// <summary>
    /// Returns to the card when the selected individual changes.
    /// </summary>
    protected override void OnParametersSet()
    {
        string? pokemonId = SelectedEnemy?.EnemyId;
        if (_defensePokemonId != pokemonId || _dialogBattleId != BattleId)
        {
            _defenseOpen = false;
            _abilitiesOpen = false;
        }

        _defensePokemonId = pokemonId;
        _dialogBattleId = BattleId;
    }

    /// <summary>
    /// Opens the selected Pokemon's live defense overview.
    /// </summary>
    private void OpenDefense()
    {
        _abilitiesOpen = false;
        _defenseOpen = true;
    }

    /// <summary>
    /// Returns from the defense overview to the Pokemon card.
    /// </summary>
    private void CloseDefense()
        => _defenseOpen = false;

    /// <summary>
    /// Gets or initializes tracker-owned run knowledge.
    /// </summary>
    [Inject]
    private TrackerKnowledgeStore Knowledge { get; set; } = null!;

    /// <summary>
    /// Gets or sets the active opposing Pokemon.
    /// </summary>
    [Parameter]
    public IReadOnlyList<EnemyPokemonSnapshot> Enemies { get; set; } = [];

    /// <summary>
    /// Gets or sets the active battle identifier.
    /// </summary>
    [Parameter]
    public string? BattleId { get; set; }

    /// <summary>
    /// Gets or sets the current save's individual gym badge flags.
    /// </summary>
    [Parameter]
    public IReadOnlyList<bool>? Badges { get; set; }

    /// <summary>
    /// Gets or sets the selected opposing Pokemon identifier.
    /// </summary>
    [Parameter]
    public string? SelectedEnemyId { get; set; }

    /// <summary>
    /// Gets or sets the callback raised when the selected opposing Pokemon changes.
    /// </summary>
    [Parameter]
    public EventCallback<string?> SelectedEnemyIdChanged { get; set; }

    /// <summary>
    /// Gets or sets visible player types used for move effectiveness.
    /// </summary>
    [Parameter]
    public IReadOnlyList<string>? PlayerTypes { get; set; }

    /// <summary>
    /// Gets or sets the player's current evasion stage.
    /// </summary>
    [Parameter]
    public int PlayerEvasionStage { get; set; }

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets the selected enemy or first active enemy as a fallback.
    /// </summary>
    private EnemyPokemonSnapshot? SelectedEnemy
    {
        get
        {
            EnemyPokemonSnapshot? selected = Enemies.FirstOrDefault(enemy => enemy.EnemyId == SelectedEnemyId);
            return selected ?? (Enemies.Count > 0 ? Enemies[0] : null);
        }
    }

    /// <summary>
    /// Subscribes the card to remembered-knowledge changes.
    /// </summary>
    protected override void OnInitialized() => Knowledge.Changed += HandleKnowledgeChanged;

    /// <summary>
    /// Selects one active opposing Pokemon.
    /// </summary>
    /// <param name="enemyId">The battle-stable enemy identifier.</param>
    private Task SelectEnemy(string enemyId)
        => SelectedEnemyIdChanged.InvokeAsync(enemyId);

    /// <summary>
    /// Gets the visible enemy name or empty-state label.
    /// </summary>
    /// <returns>The card heading.</returns>
    private string GetName()
        => SelectedEnemy?.SpeciesName ?? Text["Enemy.Card.NoOpponent"];

    /// <summary>
    /// Gets remembered abilities for the selected enemy.
    /// </summary>
    /// <returns>The remembered abilities in display order.</returns>
    private IReadOnlyList<AbilitySnapshot> GetAbilities()
        => SelectedEnemy is null ? [] : Knowledge.GetAbilities(SelectedEnemy.SpeciesId);

    /// <summary>
    /// Gets the first remembered ability name.
    /// </summary>
    /// <returns>The ability name or placeholder.</returns>
    private string GetFirstAbilityName()
    {
        if (SelectedEnemy is null)
            return "--";

        var abilities = GetAbilities();
        return abilities.Count > 0 ? abilities[0].Name : Text["Enemy.Card.Unknown"];
    }

    /// <summary>
    /// Formats the current ordinary Poke Ball capture chance.
    /// </summary>
    /// <param name="catchChance">The percentage supplied by the game.</param>
    /// <returns>The localized percentage label.</returns>
    private static string FormatCatchChance(double catchChance)
        => $"{catchChance.ToString("0.0", CultureInfo.CurrentCulture)}%";

    /// <summary>
    /// Formats the highest level encountered for the selected enemy species and form.
    /// </summary>
    /// <returns>The highest-level label or placeholder.</returns>
    private string FormatHighestLevel()
    {
        if (SelectedEnemy is null)
            return "--";

        int highestLevel = Math.Max(SelectedEnemy.Level, Knowledge.GetHighestLevel(SelectedEnemy.SpeciesId));
        return Text["Enemy.Card.LevelValue", highestLevel];
    }

    /// <summary>
    /// Refreshes the card after remembered knowledge or annotations change.
    /// </summary>
    /// <param name="sender">The knowledge store raising the event.</param>
    /// <param name="args">The change event arguments.</param>
    private void HandleKnowledgeChanged(object? sender, EventArgs args)
        => _ = InvokeAsync(StateHasChanged);

    /// <summary>
    /// Opens descriptions of all remembered abilities for the selected species.
    /// </summary>
    private void OpenAbilities()
        => _abilitiesOpen = GetAbilities().Count > 0;

    /// <summary>
    /// Closes the selected ability information.
    /// </summary>
    private void CloseAbilities()
        => _abilitiesOpen = false;

    /// <summary>
    /// Removes the remembered-knowledge subscription when the card is disposed.
    /// </summary>
    public void Dispose()
        => Knowledge.Changed -= HandleKnowledgeChanged;
}
