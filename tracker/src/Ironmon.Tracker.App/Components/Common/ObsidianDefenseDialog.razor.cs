using Ironmon.Tracker.Protocol.Pokemon;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Shows type-specific factors, broad effects and complete protection/recovery information in a modal.
/// </summary>
public partial class ObsidianDefenseDialog
{
    private const string _physicalCategory = "physical";
    private const string _specialCategory = "special";
    private const string _weaknessesKey = "Defense.Weaknesses";
    private const string _resistancesKey = "Defense.Resistances";
    private const string _immunitiesKey = "Defense.Immunities";
    private const string _variesKey = "Defense.Varies";
    private const string _physicalLabelKey = "Common.Move.Physical";
    private const string _specialLabelKey = "Common.Move.Special";
    private const string _bothLabelKey = "Redesign.BothCategories";
    private static readonly string[] _groups = [_weaknessesKey, _resistancesKey, _immunitiesKey, _variesKey];
    private string? _selectedType;

    /// <summary>
    /// Gets or sets the visible Pokemon name.
    /// </summary>
    [Parameter]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game-produced defensive information.
    /// </summary>
    [Parameter]
    public DefenseOverviewSnapshot? Defense { get; set; }

    /// <summary>
    /// Gets or sets the dismissal callback.
    /// </summary>
    [Parameter]
    public EventCallback Closed { get; set; }

    /// <summary>
    /// Gets the selected type, or the first non-neutral type for the combined example.
    /// </summary>
    /// <returns>The selected type snapshot or first non-neutral type for the combined example.</returns>
    private DefenseTypeSnapshot? SelectedType()
    {
        var snapshot = Defense?.TypeMatchups.FirstOrDefault(entry => entry.Type == _selectedType)
            ?? Defense?.TypeMatchups.FirstOrDefault(entry => new DefenseMatchupPresentation(entry).Group() is not null);

        if (snapshot is not null)
            return snapshot;

        if (Defense?.TypeMatchups.Count == default)
            return null;

        return Defense?.TypeMatchups[0];
    }

    /// <summary>
    /// Gets non-neutral entries in a display group.
    /// </summary>
    /// <param name="group">The localization key identifying the defensive outcome group.</param>
    /// <returns>The non-neutral type projections, ordered by maximum damage factor.</returns>
    private IEnumerable<DefenseMatchupPresentation> Matching(string group) 
        => (Defense?.TypeMatchups ?? []).Select(entry => new DefenseMatchupPresentation(entry)).Where(entry => entry.Group() == group).OrderByDescending(entry => entry.Maximum);

    /// <summary>
    /// Selects a type for the composed-factor explanation.
    /// </summary>
    /// <param name="type">The incoming attack type identifier.</param>
    private void SelectType(string type) 
        => _selectedType = type;

    /// <summary>
    /// Gets the category label for a broad effect.
    /// </summary>
    /// <param name="category">The affected move category, or null for both categories.</param>
    /// <returns>The localized category label.</returns>
    private string CategoryLabel(string? category) 
        => category == _physicalCategory ? Text[_physicalLabelKey] : category == _specialCategory ? Text[_specialLabelKey] : Text[_bothLabelKey];
}
