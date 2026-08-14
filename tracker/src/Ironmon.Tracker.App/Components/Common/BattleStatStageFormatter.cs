using System.Globalization;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Formats battle stat stages and their effective multipliers.
/// </summary>
internal static class BattleStatStageFormatter
{
    /// <summary>
    /// Formats a nonzero stage as a signed value for a labeled chip.
    /// </summary>
    /// <param name="stage">The stage from minus six through plus six.</param>
    /// <returns>The signed stage, or an empty string at neutral.</returns>
    internal static string FormatSignedStage(int stage)
    {
        int validated = Math.Clamp(stage, -6, 6);
        return validated switch
        {
            > 0 => $"+{validated}",
            < 0 => $"−{Math.Abs(validated)}",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Formats a nonzero stage as its effective multiplier.
    /// </summary>
    /// <param name="stage">The stage from minus six through plus six.</param>
    /// <returns>The effective multiplier, or an empty string at neutral.</returns>
    internal static string FormatMultiplier(int stage)
    {
        int validated = Math.Clamp(stage, -6, 6);
        if (validated == 0)
            return string.Empty;

        decimal multiplier = validated > 0 ? (2m + validated) / 2m : 2m / (2m - validated);
        return $"×{multiplier.ToString("0.##", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Formats the exact effective multiplier for a tooltip.
    /// </summary>
    /// <param name="stage">The stage from minus six through plus six.</param>
    /// <returns>The effective multiplier, or an empty string at neutral.</returns>
    internal static string FormatTooltip(int stage) => FormatMultiplier(stage);
}
