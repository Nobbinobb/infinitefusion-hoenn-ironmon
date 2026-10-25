using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents an authored encounter slot or a chance-based fusion while respecting disclosure.
/// </summary>
public partial class ObsidianLookupEncounter
{
    private const string _overworldPrefix = "overworld";
    private const string _overworldKey = "Lookup.Areas.FusionOrigin.Overworld";
    private const string _standardKey = "Lookup.Areas.FusionOrigin.Standard";
    private const string _levelKey = "Lookup.Areas.Level";
    private const string _rangeKey = "Lookup.Areas.LevelRange";

    /// <summary>
    /// Gets or sets an authored encounter slot, or null when displaying a fusion possibility.
    /// </summary>
    [Parameter]
    public AreaEncounterEntryPayload? Encounter { get; set; }

    /// <summary>
    /// Gets or sets a chance-based fusion, or null when displaying an authored slot.
    /// </summary>
    [Parameter]
    public AreaEncounterFusionEntryPayload? Fusion { get; set; }

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets the disclosed slot identity with its stable identifier as fallback.
    /// </summary>
    private string Identity => Encounter?.SpeciesName ?? Encounter?.SpeciesId ?? string.Empty;

    /// <summary>
    /// Gets the localized encounter mechanic for the displayed chance fusion.
    /// </summary>
    private string Origin => Text[Fusion?.Origin.StartsWith(_overworldPrefix, StringComparison.Ordinal) == true ? _overworldKey : _standardKey];

    /// <summary>
    /// Formats a single level or inclusive level range.
    /// </summary>
    /// <param name="minimum">The lowest possible level.</param>
    /// <param name="maximum">The highest possible level.</param>
    /// <returns>The localized level description.</returns>
    private string LevelRange(int minimum, int maximum)
        => minimum == maximum ? Text[_levelKey, minimum] : Text[_rangeKey, minimum, maximum];
}
