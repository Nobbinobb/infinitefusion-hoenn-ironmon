using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents authoritative statistics for a current or completed Ironmon attempt.
/// </summary>
public partial class RunStatistics
{
    /// <summary>
    /// Gets or sets the statistics to display.
    /// </summary>
    [Parameter]
    public RunStatisticsPayload? Statistics { get; set; }

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
        return $"{string.Join(", ", statistics.TrainerSpeciesMostEncountered.Select(FormatIdentifier))} \u00B7 {count}";
    }
}
