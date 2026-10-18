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
    /// <param name="usesAccuracyFormula">Whether to use the accuracy and evasion stage scale.</param>
    /// <returns>The effective multiplier, or an empty string at neutral.</returns>
    internal static string FormatMultiplier(int stage, bool usesAccuracyFormula = false)
    {
        int validated = Math.Clamp(stage, -6, 6);
        if (validated == 0)
            return string.Empty;

        decimal basis = usesAccuracyFormula ? 3m : 2m;
        decimal multiplier = validated > 0 ? (basis + validated) / basis : basis / (basis - validated);
        return $"×{multiplier.ToString("0.##", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Formats the exact effective multiplier for a tooltip.
    /// </summary>
    /// <param name="stage">The stage from minus six through plus six.</param>
    /// <param name="usesAccuracyFormula">Whether to use the accuracy and evasion stage scale.</param>
    /// <returns>The effective multiplier, or an empty string at neutral.</returns>
    internal static string FormatTooltip(int stage, bool usesAccuracyFormula = false)
        => FormatMultiplier(stage, usesAccuracyFormula);
}
