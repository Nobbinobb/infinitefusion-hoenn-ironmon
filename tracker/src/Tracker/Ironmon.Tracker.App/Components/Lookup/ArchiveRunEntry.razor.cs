using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents recorded run identity and outcome consistently in navigation and history.
/// </summary>
public partial class ArchiveRunEntry
{
    private const string _lostResult = "lost";
    private const string _wonResult = "won";
    private const string _abandonedResult = "abandoned";
    private const string _lostKey = "Lookup.Statistics.Lost";
    private const string _wonKey = "Lookup.Statistics.Won";
    private const string _abandonedKey = "Lookup.Statistics.Abandoned";

    /// <summary>
    /// Gets or sets the recorded run represented by this entry.
    /// </summary>
    [Parameter, EditorRequired]
    public CompletedRunRecipePayload Recipe { get; set; } = null!;

    /// <summary>
    /// Gets a localized result, retaining unknown protocol outcomes verbatim.
    /// </summary>
    private string ResultLabel => Recipe.Result.ToLowerInvariant() switch
    {
        _lostResult => Text[_lostKey],
        _wonResult => Text[_wonKey],
        _abandonedResult => Text[_abandonedKey],
        _ => Recipe.Result
    };

    /// <summary>
    /// Formats recorded active play time.
    /// </summary>
    /// <param name="seconds">Recorded active seconds.</param>
    /// <returns>Hours, minutes, and seconds of active play.</returns>
    private static string FormatDuration(double seconds)
    {
        TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
        return $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}";
    }
}
