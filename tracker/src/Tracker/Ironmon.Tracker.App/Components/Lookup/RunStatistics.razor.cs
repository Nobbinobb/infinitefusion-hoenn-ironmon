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
    /// Formats an optional average BST.
    /// </summary>
    /// <param name="value">The optional average.</param>
    /// <returns>The rounded average or an unavailable marker.</returns>
    private static string FormatAverage(double? value)
        => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "\u2014";

}
