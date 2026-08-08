using System.Globalization;

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
    /// Formats move power for status moves.
    /// </summary>
    /// <param name="power">The base move power.</param>
    /// <returns>The display power.</returns>
    public static string FormatPower(int power)
        => power == 0 ? "— Pow" : $"{power} Pow";

    /// <summary>
    /// Formats move accuracy for always-hit moves.
    /// </summary>
    /// <param name="accuracy">The move accuracy.</param>
    /// <returns>The display accuracy.</returns>
    public static string FormatAccuracy(int accuracy)
        => accuracy == 0 ? "— Acc" : $"{accuracy.ToString(CultureInfo.InvariantCulture)}% Acc";

    /// <summary>
    /// Gets the compact effectiveness symbol.
    /// </summary>
    /// <param name="effectiveness">The calculated effectiveness.</param>
    /// <returns>The arrow or immunity symbol.</returns>
    public static string GetEffectivenessSymbol(MoveEffectiveness effectiveness) => effectiveness switch
    {
        MoveEffectiveness.Double => "↑",
        MoveEffectiveness.Quadruple => "↑↑",
        MoveEffectiveness.Half => "↓",
        MoveEffectiveness.Quarter => "↓↓",
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
        MoveEffectiveness.Double or MoveEffectiveness.Quadruple => "effectiveness increased",
        MoveEffectiveness.Half or MoveEffectiveness.Quarter => "effectiveness decreased",
        MoveEffectiveness.Immune => "effectiveness immune",
        _ => "effectiveness"
    };

    /// <summary>
    /// Gets accessible detail for an effectiveness symbol.
    /// </summary>
    /// <param name="effectiveness">The calculated effectiveness.</param>
    /// <returns>The effectiveness description.</returns>
    public static string GetEffectivenessTitle(MoveEffectiveness effectiveness) => effectiveness switch
    {
        MoveEffectiveness.Double => "Super effective (2×)",
        MoveEffectiveness.Quadruple => "Super effective (4×)",
        MoveEffectiveness.Half => "Not very effective (0.5×)",
        MoveEffectiveness.Quarter => "Not very effective (0.25×)",
        MoveEffectiveness.Immune => "No effect",
        _ => "Normally effective"
    };
}
