using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Presents instance IVs independently from species base stats and their display modes.
/// </summary>
public partial class PokemonIndividualValues
{
    private const int _maximumIv = 31;
    private const string _unknownKey = "Stats.IVs.Unknown";
    private const string _hp = "HP";
    private const string _specialAttack = "SPA";
    private const string _specialDefense = "SPD";
    private const string _attack = "ATK";
    private const string _defense = "DEF";
    private const string _speed = "SPE";
    private const string _barFormat = "0.##";

    /// <summary>
    /// Gets or sets optional recorded-individual selection content.
    /// </summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Gets or sets the known raw IVs, or null when no individual is represented.
    /// </summary>
    [Parameter]
    public IndividualValuesSnapshot? Ivs { get; set; }

    /// <summary>
    /// Gets or sets optional effective IVs used by stat calculation.
    /// </summary>
    [Parameter]
    public IndividualValuesSnapshot? EffectiveIvs { get; set; }

    /// <summary>
    /// Checks whether a vector contains at least one recorded IV in the valid range.
    /// </summary>
    /// <param name="values">The optional vector to inspect.</param>
    /// <returns>Whether any known IV can be displayed.</returns>
    public static bool HasKnownValues(IndividualValuesSnapshot? values)
        => values is not null && new int?[] { values.Hp, values.Attack, values.Defense, values.SpecialAttack, values.SpecialDefense, values.Speed }.Any(value => value is >= 0 and <= _maximumIv);

    /// <summary>
    /// Converts a known IV to a percentage of 31 without inventing a bar for unknown values.
    /// </summary>
    /// <param name="value">The IV, or null when unknown.</param>
    /// <returns>The percentage width, or zero for missing and invalid values.</returns>
    private static string GetBarWidth(int? value)
        => (value is >= 0 and <= _maximumIv ? value.Value * 100m / _maximumIv : decimal.Zero).ToString(_barFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets whether known effective IVs differ from the underlying values.
    /// </summary>
    private bool HasEffectiveChanges => EffectiveIvs is not null && GetValues().Any(stat => stat.Effective is >= 0 and <= _maximumIv && stat.Effective != stat.Raw);

    /// <summary>
    /// Gets all six IVs in the existing game display order.
    /// </summary>
    /// <returns>The stat labels and nullable raw and effective IVs.</returns>
    private IReadOnlyList<(string Name, int? Raw, int? Effective)> GetValues() =>
    [
        (_hp, Ivs?.Hp, EffectiveIvs?.Hp),
        (_specialAttack, Ivs?.SpecialAttack, EffectiveIvs?.SpecialAttack),
        (_specialDefense, Ivs?.SpecialDefense, EffectiveIvs?.SpecialDefense),
        (_attack, Ivs?.Attack, EffectiveIvs?.Attack),
        (_defense, Ivs?.Defense, EffectiveIvs?.Defense),
        (_speed, Ivs?.Speed, EffectiveIvs?.Speed)
    ];

    /// <summary>
    /// Formats a valid IV without substituting zero for missing or invalid data.
    /// </summary>
    /// <param name="value">The raw value, or null when unknown.</param>
    /// <returns>The IV or the localized unknown-value marker.</returns>
    private string FormatValue(int? value)
        => value is >= 0 and <= _maximumIv ? value.Value.ToString(CultureInfo.InvariantCulture) : Text[_unknownKey].Value;
}
