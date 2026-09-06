using Ironmon.Tracker.Protocol.Pokemon;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Explains the known nature adjustments with full stat names and signed percentages.
/// </summary>
public partial class ObsidianNatureDialog
{
    private const string _attackKey = "Redesign.Nature.Attack";
    private const string _defenseKey = "Redesign.Nature.Defense";
    private const string _specialAttackKey = "Redesign.Nature.SpecialAttack";
    private const string _specialDefenseKey = "Redesign.Nature.SpecialDefense";
    private const string _speedKey = "Redesign.Nature.Speed";
    private const string _increasedKey = "Redesign.Nature.Increased";
    private const string _decreasedKey = "Redesign.Nature.Decreased";
    private const string _neutralKey = "Player.Card.NatureNoStatChange";
    private const string _transformedKey = "Redesign.TransformedNature";
    private const string _sentenceSeparator = " ";
    private const string _positiveClass = "obsidian-positive";
    private const string _negativeClass = "obsidian-negative";
    private const string _increaseValueKey = "Redesign.Nature.IncreaseValue";
    private const string _decreaseValueKey = "Redesign.Nature.DecreaseValue";

    /// <summary>
    /// Gets or sets the localized nature name.
    /// </summary>
    [Parameter]
    public string Nature { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game-provided nature adjustments.
    /// </summary>
    [Parameter]
    public NatureAdjustmentsSnapshot Adjustments { get; set; } = new();

    /// <summary>
    /// Gets or sets whether copied battle stats make the original nature inapplicable.
    /// </summary>
    [Parameter]
    public bool Transformed { get; set; }

    /// <summary>
    /// Gets or sets the callback raised when the dialog should close.
    /// </summary>
    [Parameter]
    public EventCallback Closed { get; set; }

    /// <summary>
    /// Gets the non-neutral adjustments with increased stats before decreased stats.
    /// </summary>
    /// <returns>The localized stat resource keys and their known adjustments.</returns>
    private IReadOnlyList<(string NameKey, StatAdjustment Adjustment)> GetAdjustments()
    {
        (string NameKey, StatAdjustment Adjustment)[] stats =
        [
            (_attackKey, Adjustments.Attack),
            (_defenseKey, Adjustments.Defense),
            (_specialAttackKey, Adjustments.SpecialAttack),
            (_specialDefenseKey, Adjustments.SpecialDefense),
            (_speedKey, Adjustments.Speed)
        ];

        return [.. stats.Where(stat => stat.Adjustment != StatAdjustment.Neutral)
            .OrderBy(stat => stat.Adjustment == StatAdjustment.Increased ? 0 : 1)];
    }

    /// <summary>
    /// Explains the nature without attributing original adjustments to transformed stats.
    /// </summary>
    /// <returns>The localized increase and decrease sentences, or the applicable fallback.</returns>
    private string GetDescription()
    {
        if (Transformed)
            return Text[_transformedKey];

        var stats = GetAdjustments();
        if (stats.Count == 0)
            return Text[_neutralKey];

        return string.Join(_sentenceSeparator, stats.Select(stat => Text[
            stat.Adjustment == StatAdjustment.Increased ? _increasedKey : _decreasedKey,
            Text[stat.NameKey]].Value));
    }
}
