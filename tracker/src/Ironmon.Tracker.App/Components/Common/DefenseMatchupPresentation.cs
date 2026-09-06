using System.Globalization;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Projects game-produced factors without inferring mechanics from names or descriptions.
/// </summary>
/// <remarks>
/// Initializes a projection from a game-produced matchup and the requested factor scope.
/// </remarks>
/// <param name="entry">The privacy-filtered type snapshot.</param>
/// <param name="beforeAllTypes">Whether to use factors before broad effects, with legacy fallback.</param>
public sealed class DefenseMatchupPresentation(DefenseTypeSnapshot entry, bool beforeAllTypes = true)
{
    private const string _weaknesses = "Defense.Weaknesses";
    private const string _resistances = "Defense.Resistances";
    private const string _immunities = "Defense.Immunities";
    private const string _varies = "Defense.Varies";
    private const string _numberFormat = "0.###";

    /// <summary>
    /// Gets the underlying type information.
    /// </summary>
    public DefenseTypeSnapshot Entry { get; } = entry;

    /// <summary>
    /// Gets the lower physical factor.
    /// </summary>
    public decimal PhysicalMin => Math.Round((beforeAllTypes ? Entry.TypePhysicalMin : null) ?? Entry.PhysicalMin ?? Entry.Multiplier, 6);

    /// <summary>
    /// Gets the upper physical factor.
    /// </summary>
    public decimal PhysicalMax => Math.Round((beforeAllTypes ? Entry.TypePhysicalMax : null) ?? Entry.PhysicalMax ?? Entry.Multiplier, 6);

    /// <summary>
    /// Gets the lower special factor.
    /// </summary>
    public decimal SpecialMin => Math.Round((beforeAllTypes ? Entry.TypeSpecialMin : null) ?? Entry.SpecialMin ?? Entry.Multiplier, 6);

    /// <summary>
    /// Gets the upper special factor.
    /// </summary>
    public decimal SpecialMax => Math.Round((beforeAllTypes ? Entry.TypeSpecialMax : null) ?? Entry.SpecialMax ?? Entry.Multiplier, 6);

    /// <summary>
    /// Gets whether one value describes both move categories.
    /// </summary>
    public bool SameCategories => PhysicalMin == SpecialMin && PhysicalMax == SpecialMax;

    /// <summary>
    /// Gets whether the physical category is non-neutral.
    /// </summary>
    public bool ShowPhysical => PhysicalMin != 1 || PhysicalMax != 1;

    /// <summary>
    /// Gets whether the special category is non-neutral.
    /// </summary>
    public bool ShowSpecial => SpecialMin != 1 || SpecialMax != 1;

    /// <summary>
    /// Gets the largest displayed factor for sorting.
    /// </summary>
    public decimal Maximum => Math.Max(PhysicalMax, SpecialMax);

    /// <summary>
    /// Gets the defense section, omitting completely neutral entries.
    /// </summary>
    /// <returns>The defesne section.</returns>
    public string? Group()
    {
        if (Maximum == 0)
            return _immunities;

        if (!ShowPhysical && !ShowSpecial)
            return null;

        if (Math.Min(PhysicalMin, SpecialMin) >= 1)
            return _weaknesses;

        if (Maximum <= 1)
            return _resistances;

        return _varies;
    }

    /// <summary>
    /// Formats a multiplier or conditional range without losing third-type fractions.
    /// </summary>
    /// <param name="min">The lower bound of the damage factor.</param>
    /// <param name="max">The upper bound of the damage factor.</param>
    /// <returns>The localized multiplier or conditional multiplier range.</returns>
    public static string Format(decimal min, decimal max)
        => min == max ? $"{min.ToString(_numberFormat, CultureInfo.CurrentCulture)}×" : $"{min.ToString(_numberFormat, CultureInfo.CurrentCulture)}–{max.ToString(_numberFormat, CultureInfo.CurrentCulture)}×";
}
