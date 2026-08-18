using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents authoritative statistics for a current or completed Ironmon attempt.
/// </summary>
public partial class RunStatistics
{
    private RunStatisticsSection _selectedSection = RunStatisticsSection.Overview;

    /// <summary>
    /// Gets or sets the statistics to display.
    /// </summary>
    [Parameter]
    public RunStatisticsPayload? Statistics { get; set; }

    /// <summary>
    /// Selects one run-statistics section.
    /// </summary>
    /// <param name="section">The selected section.</param>
    private void SelectSection(RunStatisticsSection section)
        => _selectedSection = section;

    /// <summary>
    /// Gets the localized name of one run-statistics section.
    /// </summary>
    /// <param name="section">The section to name.</param>
    /// <returns>The localized section name.</returns>
    private string GetSectionName(RunStatisticsSection section) => section switch
    {
        RunStatisticsSection.Overview => Text["Lookup.Statistics.OverviewTab"],
        RunStatisticsSection.Trainers => Text["Lookup.Statistics.TrainersTab"],
        RunStatisticsSection.Items => Text["Lookup.Statistics.ItemsTab"],
        _ => string.Empty
    };

    /// <summary>
    /// Gets the visual classes for one statistics tab.
    /// </summary>
    /// <param name="section">The represented section.</param>
    /// <returns>The tab classes.</returns>
    private string GetSectionTabClass(RunStatisticsSection section)
        => section == _selectedSection ? "statistics-tab selected" : "statistics-tab";

    /// <summary>
    /// Formats accumulated active play time without fractional seconds.
    /// </summary>
    /// <param name="seconds">The accumulated active seconds.</param>
    /// <returns>The compact duration.</returns>
    private static string FormatDuration(double seconds)
    {
        TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
    }

    /// <summary>
    /// Formats a protocol identifier for human-readable display.
    /// </summary>
    /// <param name="identifier">The item or species identifier.</param>
    /// <returns>The display label.</returns>
    private static string FormatIdentifier(string identifier)
    {
        string spaced = identifier.Replace('_', ' ').ToLowerInvariant();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced);
    }

    /// <summary>
    /// Formats a trainer species with its authoritative display name and identifier.
    /// </summary>
    /// <param name="statistics">The complete statistics payload.</param>
    /// <param name="identifier">The species identifier.</param>
    /// <returns>The combined display name and species identifier.</returns>
    private static string FormatTrainerSpecies(RunStatisticsPayload statistics, string identifier)
    {
        if (!statistics.TrainerSpeciesNames.TryGetValue(identifier, out string? name) || string.IsNullOrWhiteSpace(name))
            return FormatIdentifier(identifier);

        return $"{name} ({identifier})";
    }

    /// <summary>
    /// Formats an optional average BST.
    /// </summary>
    /// <param name="value">The optional average.</param>
    /// <returns>The rounded average or an unavailable marker.</returns>
    private static string FormatAverage(double? value)
        => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "\u2014";

    /// <summary>
    /// Formats a BST boundary and all species tied at that value.
    /// </summary>
    /// <param name="value">The optional boundary value.</param>
    /// <param name="species">The tied species identifiers.</param>
    /// <returns>The combined boundary label.</returns>
    private static string FormatBoundary(int? value, IReadOnlyList<string> species)
    {
        if (value is null)
            return "\u2014";

        string names = string.Join(", ", species.Select(FormatIdentifier));
        return names.Length == 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : $"{value} \u00B7 {names}";
    }

    /// <summary>
    /// Formats every species tied for most encounters and its frequency.
    /// </summary>
    /// <param name="statistics">The complete statistics payload.</param>
    /// <returns>The combined species-frequency label.</returns>
    private static string FormatMostEncountered(RunStatisticsPayload statistics)
    {
        if (statistics.TrainerSpeciesMostEncountered.Count == 0)
            return "\u2014";

        int count = statistics.TrainerSpeciesMostEncountered.Max(species => statistics.TrainerSpeciesCounts.TryGetValue(species, out int frequency) ? frequency : 0);
        return $"{string.Join(", ", statistics.TrainerSpeciesMostEncountered.Select(species => FormatTrainerSpecies(statistics, species)))} \u00B7 {count}";
    }
}
