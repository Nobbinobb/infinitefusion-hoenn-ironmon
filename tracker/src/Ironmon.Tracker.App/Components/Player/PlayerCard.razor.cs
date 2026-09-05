using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Player;

/// <summary>
/// Renders the complete active-player tracker card.
/// </summary>
public partial class PlayerCard
{
    private const string _activeFusionResourceKey = "Redesign.YourFusion";
    private const string _activePokemonResourceKey = "Redesign.YourPokemon";
    private const string _transformedFusionResourceKey = "Player.Card.TransformedFusion";
    private const string _transformedResourceKey = "Player.Card.Transformed";
    private const string _hpStat = "HP";
    private const string _spaStat = "SPA";
    private const string _spdStat = "SPD";
    private const string _atkStat = "ATK";
    private const string _defStat = "DEF";
    private const string _speStat = "SPE";
    private const string _bstStat = "BST";
    private const string _unknownStat = "—";
    private const string _emptyHpStyle = "width: 0%";
    private bool _itemsOpen;
    private bool _natureOpen;
    private bool _heldItemOpen;
    private bool _evolutionsOpen;
    private AbilitySnapshot? _selectedAbility;
    private bool _defenseOpen;
    private string? _defensePokemonId;

    /// <summary>
    /// Returns to the card when the selected individual changes.
    /// </summary>
    protected override void OnParametersSet()
    {
        string? pokemonId = Player?.PokemonId;
        if (_defensePokemonId != pokemonId)
        {
            _defenseOpen = false;
            _selectedAbility = null;
            _itemsOpen = false;
            CloseSupplement();
        }

        _defensePokemonId = pokemonId;
    }

    /// <summary>
    /// Opens the selected Pokemon's live defense overview.
    /// </summary>
    private void OpenDefense()
    {
        _selectedAbility = null;
        _itemsOpen = false;
        _defenseOpen = true;
    }

    /// <summary>
    /// Returns from the defense overview to the Pokemon card.
    /// </summary>
    private void CloseDefense() => _defenseOpen = false;

    /// <summary>
    /// Gets or sets the active player Pokemon.
    /// </summary>
    [Parameter]
    public PlayerPokemonSnapshot? Player { get; set; }

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets visible opposing types used for move effectiveness.
    /// </summary>
    [Parameter]
    public IReadOnlyList<string>? TargetTypes { get; set; }

    /// <summary>
    /// Gets or sets the selected target's current evasion stage.
    /// </summary>
    [Parameter]
    public int TargetEvasionStage { get; set; }

    /// <summary>
    /// Gets or sets whether the game currently has an active battle.
    /// </summary>
    [Parameter]
    public bool InBattle { get; set; }

    /// <summary>
    /// Gets or sets the active battle identifier.
    /// </summary>
    [Parameter]
    public string? BattleId { get; set; }

    /// <summary>
    /// Gets or sets the selected opposing battler position for targeted items.
    /// </summary>
    [Parameter]
    public int? TargetPosition { get; set; }

    /// <summary>
    /// Gets the visible player name or waiting label.
    /// </summary>
    /// <returns>The card heading.</returns>
    private string GetName()
        => Player?.Nickname ?? Text["Player.Card.WaitingForPlayer"];

    /// <summary>
    /// Gets whether the species name adds information beyond the visible nickname.
    /// </summary>
    /// <returns>Whether to show the species as secondary identity metadata.</returns>
    private bool HasDistinctNickname()
        => Player is not null && !string.Equals(Player.Nickname, Player.SpeciesName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the active, transformed, and fusion-aware card heading.
    /// </summary>
    /// <returns>The localized card heading.</returns>
    private string GetCardKicker()
    {
        if (Player?.Transformed == true)
            return Text[Player.Fusion ? _transformedFusionResourceKey : _transformedResourceKey];

        return Text[Player?.Fusion == true ? _activeFusionResourceKey : _activePokemonResourceKey];
    }

    /// <summary>
    /// Gets the first visible evolution requirement.
    /// </summary>
    /// <returns>The evolution requirement or placeholder.</returns>
    private string GetEvolutionRequirement()
    {
        if (Player is null)
            return "--";

        return Player.Evolutions.Count > 0 ? Player.Evolutions[0].Requirement : Text["Player.Card.None"];
    }

    /// <summary>
    /// Gets the current and maximum HP display.
    /// </summary>
    /// <returns>The HP text.</returns>
    private string GetHpText()
        => Player is null ? "-- / --" : $"{Player.CurrentHp} / {Player.MaximumHp}";

    /// <summary>
    /// Gets the accessible HP-bar label.
    /// </summary>
    /// <returns>The HP label.</returns>
    private string GetHpLabel()
        => Player is null ? Text["Player.Card.HpUnknown"] : Text["Player.Card.HpCurrentOfMaximum", Player.CurrentHp, Player.MaximumHp];

    /// <summary>
    /// Gets a culture-independent CSS width for current HP.
    /// </summary>
    /// <returns>The inline HP width declaration.</returns>
    private string? GetHpStyle()
    {
        if (Player is null)
            return _emptyHpStyle;

        double percentage = Player.MaximumHp <= 0 ? 0 : Math.Clamp(Player.CurrentHp * TrackerUiConstants.FullPercentage / (double)Player.MaximumHp, 0, TrackerUiConstants.FullPercentage);
        return string.Create(CultureInfo.InvariantCulture, $"width: {percentage:0.##}%");
    }

    /// <summary>
    /// Gets the combined persistent status and temporary confusion condition.
    /// </summary>
    /// <returns>The current condition text.</returns>
    private string GetConditionText()
    {
        if (Player is null)
            return Text["Player.Card.StatusUnknown"];

        string status = FormatStatus(Player.Status);
        if (!Player.Confused)
            return status;

        return status == Text["Player.Card.Healthy"] ? Text["Player.Card.Confused"] : Text["Player.Card.StatusWithConfusion", status];
    }

    /// <summary>
    /// Formats a game status identifier for display.
    /// </summary>
    /// <param name="status">The game status identifier.</param>
    /// <returns>The display status.</returns>
    private string FormatStatus(string status) => status.ToUpperInvariant() switch
    {
        "NONE" => Text["Player.Card.Healthy"],
        "POISON" => Text["Player.Card.Poisoned"],
        "BURN" => Text["Player.Card.Burned"],
        "PARALYSIS" => Text["Player.Card.Paralyzed"],
        "SLEEP" => Text["Player.Card.Asleep"],
        "FROZEN" => Text["Player.Card.Frozen"],
        _ => status
    };

    /// <summary>
    /// Gets the healing inventory count and percentage.
    /// </summary>
    /// <returns>The healing summary.</returns>
    private string GetHealingText()
        => Player is null ? "-- · --%" : $"{Player.Healing.ItemCount} · {Player.Healing.Percentage:0.#}%";

    /// <summary>
    /// Gets the nature's increased and decreased stat abbreviations.
    /// </summary>
    /// <returns>The nature adjustment tooltip, or an empty string without a player.</returns>
    private string GetNatureTooltip()
    {
        if (Player is null)
            return string.Empty;

        if (Player.Transformed)
            return string.Empty;

        NatureAdjustmentsSnapshot adjustments = Player.NatureAdjustments;
        (string Name, StatAdjustment Adjustment)[] stats =
        [
            ("ATK", adjustments.Attack),
            ("DEF", adjustments.Defense),
            ("SPA", adjustments.SpecialAttack),
            ("SPD", adjustments.SpecialDefense),
            ("SPE", adjustments.Speed)
        ];

        List<string> description = [];
        string? increased = stats.FirstOrDefault(stat => stat.Adjustment == StatAdjustment.Increased).Name;
        string? decreased = stats.FirstOrDefault(stat => stat.Adjustment == StatAdjustment.Decreased).Name;
        if (increased is not null)
            description.Add($"+{increased}");

        if (decreased is not null)
            description.Add($"−{decreased}");

        return description.Count == 0 ? Text["Player.Card.NatureNoStatChange"] : string.Join(" / ", description);
    }

    /// <summary>
    /// Gets the persistent moves that PP-restoring items actually target.
    /// </summary>
    /// <returns>The PP-item move targets.</returns>
    private IReadOnlyList<PlayerMoveSnapshot> GetPpItemMoves()
    {
        if (Player is null)
            return [];

        return Player.StoredMoves.Count > 0 ? Player.StoredMoves : Player.Moves;
    }

    /// <summary>
    /// Opens full information for the player's known ability.
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
    /// Opens the complete battle-item inventory.
    /// </summary>
    private void OpenItems()
        => _itemsOpen = true;

    /// <summary>
    /// Closes the complete battle-item inventory.
    /// </summary>
    private void CloseItems()
        => _itemsOpen = false;

    /// <summary>
    /// Opens the nature explanation.
    /// </summary>
    private void OpenNature() 
        => _natureOpen = true;

    /// <summary>
    /// Opens the held-item description.
    /// </summary>
    private void OpenHeldItem() 
        => _heldItemOpen = true;

    /// <summary>
    /// Opens all known evolution requirements.
    /// </summary>
    private void OpenEvolutions() 
        => _evolutionsOpen = true;

    /// <summary>
    /// Closes the supplementary identity dialog.
    /// </summary>
    private void CloseSupplement() 
        => _natureOpen = _heldItemOpen = _evolutionsOpen = false;

    /// <summary>
    /// Builds the approved stat order without attributing copied stats to the original nature.
    /// </summary>
    /// <returns>The seven visible stats in player-card display order.</returns>
    private IReadOnlyList<TrackerStatValue> GetStatValues()
    {
        NatureAdjustmentsSnapshot nature = Player?.Transformed == false ? Player.NatureAdjustments : new();
        return [new(_hpStat, FormatStat(Player?.MaximumHp)), new(_spaStat, FormatStat(Player?.SpecialAttack), nature.SpecialAttack),
            new(_spdStat, FormatStat(Player?.SpecialDefense), nature.SpecialDefense), new(_atkStat, FormatStat(Player?.Attack), nature.Attack),
            new(_defStat, FormatStat(Player?.Defense), nature.Defense), new(_speStat, FormatStat(Player?.Speed), nature.Speed),
            new(_bstStat, FormatStat(Player?.BaseStatTotal))];
    }

    /// <summary>
    /// Formats one known stat or its waiting placeholder.
    /// </summary>
    /// <param name="value">The known stat value, or null while waiting for a player.</param>
    /// <returns>The invariant stat text or unknown placeholder.</returns>
    private static string FormatStat(int? value) 
        => value?.ToString(CultureInfo.InvariantCulture) ?? _unknownStat;
}
