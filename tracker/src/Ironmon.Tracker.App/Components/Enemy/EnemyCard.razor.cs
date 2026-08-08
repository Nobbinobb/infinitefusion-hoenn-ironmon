using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Enemy;

/// <summary>
/// Renders and coordinates the selected opposing-Pokemon tracker card.
/// </summary>
public partial class EnemyCard : IDisposable
{
    private string? _spriteKey;
    private string? _spriteSource;
    private AbilitySnapshot? _selectedAbility;

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
    /// Refreshes the local sprite when the selected enemy or game changes.
    /// </summary>
    protected override void OnParametersSet()
    {
        string? key = GameRoot is null || SelectedEnemy?.SpritePath is null ? null : $"{GameRoot}|{SelectedEnemy.SpritePath}";
        if (key == _spriteKey)
            return;

        _spriteKey = key;
        _spriteSource = LocalSpriteLoader.Load(GameRoot, SelectedEnemy?.SpritePath);
    }

    /// <summary>
    /// Selects one active opposing Pokemon.
    /// </summary>
    /// <param name="enemyId">The battle-stable enemy identifier.</param>
    private Task SelectEnemy(string enemyId)
        => SelectedEnemyIdChanged.InvokeAsync(enemyId);

    /// <summary>
    /// Gets the CSS classes for one enemy selector button.
    /// </summary>
    /// <param name="enemy">The represented active enemy.</param>
    /// <returns>The selector button CSS classes.</returns>
    private string GetEnemySelectorClass(EnemyPokemonSnapshot enemy) =>
        enemy.EnemyId == SelectedEnemy?.EnemyId ? "enemy-selector-button selected" : "enemy-selector-button";

    /// <summary>
    /// Gets the visible enemy name or empty-state label.
    /// </summary>
    /// <returns>The card heading.</returns>
    private string GetName()
        => SelectedEnemy?.SpeciesName ?? "No opponent";

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
        return abilities.Count > 0 ? abilities[0].Name : "Unknown";
    }

    /// <summary>
    /// Gets the visible base-stat total.
    /// </summary>
    /// <returns>The base-stat total or placeholder.</returns>
    private string GetBaseStatTotal()
        => SelectedEnemy?.BaseStatTotal.ToString(CultureInfo.InvariantCulture) ?? "--";

    /// <summary>
    /// Formats the highest level encountered for the selected enemy species and form.
    /// </summary>
    /// <returns>The highest-level label or placeholder.</returns>
    private string FormatHighestLevel()
    {
        if (SelectedEnemy is null)
            return "--";

        int highestLevel = Math.Max(SelectedEnemy.Level, Knowledge.GetHighestLevel(SelectedEnemy.SpeciesId));
        return $"Lv. {highestLevel}";
    }

    /// <summary>
    /// Refreshes the card after remembered knowledge or annotations change.
    /// </summary>
    /// <param name="sender">The knowledge store raising the event.</param>
    /// <param name="args">The change event arguments.</param>
    private void HandleKnowledgeChanged(object? sender, EventArgs args)
        => _ = InvokeAsync(StateHasChanged);

    /// <summary>
    /// Opens full information for one discovered ability.
    /// </summary>
    /// <param name="ability">The selected ability.</param>
    private void SelectAbility(AbilitySnapshot ability)
        => _selectedAbility = ability;

    /// <summary>
    /// Closes the selected ability information.
    /// </summary>
    private void CloseAbility()
        => _selectedAbility = null;

    /// <summary>
    /// Removes the remembered-knowledge subscription when the card is disposed.
    /// </summary>
    public void Dispose()
        => Knowledge.Changed -= HandleKnowledgeChanged;
}
