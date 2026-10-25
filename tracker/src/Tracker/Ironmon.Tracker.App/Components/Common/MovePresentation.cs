namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Provides shared move-row formatting and type-only effectiveness presentation.
/// </summary>
internal static class MovePresentation
{
    /// <summary>
    /// Calculates visible type-only effectiveness when an applicable target exists.
    /// </summary>
    /// <param name="moveType">The move type.</param>
    /// <param name="category">The move category.</param>
    /// <param name="targetTypes">The legally visible target types.</param>
    /// <returns>The effectiveness, or null for status moves and absent targets.</returns>
    public static MoveEffectiveness? CalculateEffectiveness(string moveType, MoveCategory category, IReadOnlyList<string>? targetTypes)
    {
        if (category == MoveCategory.Status || targetTypes is null || targetTypes.Count == 0)
            return null;

        return TypeEffectivenessRules.Calculate(moveType, targetTypes);
    }

    /// <summary>
    /// Gets whether a damaging move receives a same-type attack bonus from its user.
    /// </summary>
    /// <param name="moveType">The move type.</param>
    /// <param name="category">The move damage category.</param>
    /// <param name="userTypes">The legally visible types of the move user.</param>
    /// <returns>Whether the move receives STAB.</returns>
    public static bool HasStab(string moveType, MoveCategory category, IReadOnlyList<string>? userTypes)
        => category != MoveCategory.Status && userTypes?.Contains(moveType, StringComparer.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Calculates displayed move accuracy after the user's accuracy and target's evasion stages.
    /// </summary>
    /// <param name="baseAccuracy">The move's base accuracy.</param>
    /// <param name="accuracyStage">The move user's accuracy stage.</param>
    /// <param name="evasionStage">The target's evasion stage.</param>
    /// <returns>The adjusted percentage, capped at 100.</returns>
    public static int CalculateAdjustedAccuracy(int baseAccuracy, int accuracyStage, int evasionStage)
    {
        decimal accuracyPercent = decimal.Round(100m * GetAccuracyStageMultiplier(accuracyStage), 0, MidpointRounding.AwayFromZero);
        decimal evasionPercent = decimal.Round(100m * GetAccuracyStageMultiplier(evasionStage), 0, MidpointRounding.AwayFromZero);
        int adjustedAccuracy = decimal.ToInt32(decimal.Round(baseAccuracy * accuracyPercent / evasionPercent, 0, MidpointRounding.AwayFromZero));
        return Math.Clamp(adjustedAccuracy, 1, 100);
    }

    /// <summary>
    /// Gets the native accuracy-scale multiplier for one clamped battle stage.
    /// </summary>
    /// <param name="stage">The stage from minus six through plus six.</param>
    /// <returns>The accuracy or evasion multiplier.</returns>
    private static decimal GetAccuracyStageMultiplier(int stage)
    {
        int validated = Math.Clamp(stage, -6, 6);
        return validated >= 0 ? (3m + validated) / 3m : 3m / (3m - validated);
    }

    /// <summary>
    /// Gets a move's type-specific CSS classes.
    /// </summary>
    /// <param name="type">The stable move type identifier.</param>
    /// <returns>The move type CSS classes.</returns>
    public static string GetMoveTypeClass(string type)
        => $"move-type {GetTypeColorClass(type)}";

    /// <summary>
    /// Gets the shared type color class for moves and Pokemon typing.
    /// </summary>
    /// <param name="type">The stable type identifier.</param>
    /// <returns>The type color CSS class.</returns>
    public static string GetTypeColorClass(string type)
    {
        string normalized = new(type.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return $"type-{normalized}";
    }

    /// <summary>
    /// Gets the compact effectiveness symbol.
    /// </summary>
    /// <param name="effectiveness">The calculated effectiveness.</param>
    /// <returns>The arrow or immunity symbol.</returns>
    public static string GetEffectivenessSymbol(MoveEffectiveness effectiveness) => effectiveness switch
    {
        MoveEffectiveness.Double => "^",
        MoveEffectiveness.Quadruple => "^^",
        MoveEffectiveness.Half => "v",
        MoveEffectiveness.Quarter => "vv",
        MoveEffectiveness.Immune => "×",
        _ => string.Empty
    };

    /// <summary>
    /// Gets the CSS class for an effectiveness symbol.
    /// </summary>
    /// <param name="effectiveness">The calculated effectiveness.</param>
    /// <returns>The effectiveness CSS classes.</returns>
    public static string GetEffectivenessClass(MoveEffectiveness effectiveness) => effectiveness switch
    {
        MoveEffectiveness.Double => "effectiveness increased single",
        MoveEffectiveness.Quadruple => "effectiveness increased double",
        MoveEffectiveness.Half => "effectiveness decreased single",
        MoveEffectiveness.Quarter => "effectiveness decreased double",
        MoveEffectiveness.Immune => "effectiveness immune",
        _ => "effectiveness"
    };

}
