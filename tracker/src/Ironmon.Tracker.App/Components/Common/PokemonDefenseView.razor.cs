using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Shows combined non-neutral matchups, compact protections, and expandable recovery triggers without source explanations or inferred concealed state.
/// </summary>
public partial class PokemonDefenseView
{
    private const string _weaknessesKey = "Defense.Weaknesses";
    private const string _resistancesKey = "Defense.Resistances";
    private const string _immunitiesKey = "Defense.Immunities";
    private const string _variesKey = "Defense.Varies";
    private const string _numberFormat = "0.###";
    private const string _escapeKey = "Escape";
    private const string _multiplierFormat = "0.###'×'";
    private ElementReference _backButton;

    /// <summary>
    /// Gets or sets the visible Pokemon name.
    /// </summary>
    [Parameter] public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the latest game-produced overview.
    /// </summary>
    [Parameter] public DefenseOverviewSnapshot? Defense { get; set; }

    /// <summary>
    /// Gets or sets the return-to-card callback.
    /// </summary>
    [Parameter] public EventCallback Closed { get; set; }

    /// <summary>
    /// Places keyboard focus on the return action when entering the overview.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            await _backButton.FocusAsync();
    }

    /// <summary>
    /// Gets the non-neutral matchups grouped by defensive outcome.
    /// </summary>
    private IReadOnlyList<(string Heading, IReadOnlyList<DefenseTypeSnapshot> Types)> MatchupGroups =>
    [
        (_weaknessesKey, Matching(_weaknessesKey)),
        (_resistancesKey, Matching(_resistancesKey)),
        (_immunitiesKey, Matching(_immunitiesKey)),
        (_variesKey, Matching(_variesKey))
    ];

    /// <summary>
    /// Gets non-neutral entries in one combined-damage group.
    /// </summary>
    private IReadOnlyList<DefenseTypeSnapshot> Matching(string group) => [.. (Defense?.TypeMatchups ?? []).Where(entry => Group(entry) == group).OrderByDescending(Maximum)];

    /// <summary>
    /// Classifies the full range of ordinary physical and special damage factors.
    /// </summary>
    private static string? Group(DefenseTypeSnapshot entry)
    {
        decimal min = Math.Min(PhysicalMin(entry), SpecialMin(entry));
        decimal max = Maximum(entry);
        if (max == 0)
            return _immunitiesKey;

        if (min == 1 && max == 1)
            return null;

        if (min >= 1)
            return _weaknessesKey;

        return max <= 1 ? _resistancesKey : _variesKey;
    }

    /// <summary>
    /// Gets the largest combined factor.
    /// </summary>
    private static decimal Maximum(DefenseTypeSnapshot entry)
        => Math.Max(PhysicalMax(entry), SpecialMax(entry));

    /// <summary>
    /// Gets the lower physical factor without floating-point boundary noise.
    /// </summary>
    private static decimal PhysicalMin(DefenseTypeSnapshot entry)
        => Math.Round(entry.PhysicalMin ?? entry.Multiplier, 6);

    /// <summary>
    /// Gets the upper physical factor without floating-point boundary noise.
    /// </summary>
    private static decimal PhysicalMax(DefenseTypeSnapshot entry)
        => Math.Round(entry.PhysicalMax ?? entry.Multiplier, 6);

    /// <summary>
    /// Gets the lower special factor without floating-point boundary noise.
    /// </summary>
    private static decimal SpecialMin(DefenseTypeSnapshot entry)
        => Math.Round(entry.SpecialMin ?? entry.Multiplier, 6);

    /// <summary>
    /// Gets the upper special factor without floating-point boundary noise.
    /// </summary>
    private static decimal SpecialMax(DefenseTypeSnapshot entry)
        => Math.Round(entry.SpecialMax ?? entry.Multiplier, 6);

    /// <summary>
    /// Determines whether both move categories share one factor range.
    /// </summary>
    private static bool SameCategories(DefenseTypeSnapshot entry)
        => PhysicalMin(entry) == SpecialMin(entry) && PhysicalMax(entry) == SpecialMax(entry);

    /// <summary>
    /// Formats a fixed factor or a conditional range.
    /// </summary>
    private static string FormatRange(decimal min, decimal max)
        => min == max ? FormatMultiplier(min) : $"{min.ToString(_numberFormat, CultureInfo.CurrentCulture)}–{FormatMultiplier(max)}";

    /// <summary>
    /// Formats multipliers without losing third-type fractions.
    /// </summary>
    private static string FormatMultiplier(decimal multiplier)
        => multiplier.ToString(_multiplierFormat, CultureInfo.CurrentCulture);

    /// <summary>
    /// Returns to the Pokemon card when Escape is pressed within the view.
    /// </summary>
    private Task HandleKeyDown(KeyboardEventArgs args)
        => args.Key == _escapeKey ? Closed.InvokeAsync() : Task.CompletedTask;
}
