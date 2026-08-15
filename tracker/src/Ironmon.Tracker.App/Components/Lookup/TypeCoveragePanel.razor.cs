using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents aggregate attacking-type coverage for the active trainer policy.
/// </summary>
public partial class TypeCoveragePanel
{
    private TypeCoverageCompatibilityStatus _compatibility = TypeCoverageCompatibilityStatus.MissingContext;
    private TypeCoverageCalculation? _calculation;
    private TypeCoverageContext? _context;

    /// <summary>
    /// Gets or initializes the packaged aggregate coverage dataset.
    /// </summary>
    [Inject]
    private TypeCoverageDataset Dataset { get; set; } = null!;

    /// <summary>
    /// Gets or sets the connected game handshake.
    /// </summary>
    [Parameter]
    public GameHandshakePayload? Game { get; set; }

    /// <summary>
    /// Gets or sets the recovered active game state.
    /// </summary>
    [Parameter]
    public GameCurrentStatePayload? State { get; set; }

    /// <summary>
    /// Gets or sets the parent-owned selection retained across Lookup tab changes.
    /// </summary>
    [Parameter]
    public required TypeCoverageSelectionState Selection { get; set; }

    /// <summary>
    /// Re-evaluates compatibility and calculation after connected state changes.
    /// </summary>
    protected override void OnParametersSet()
    {
        _context = TrackerTypeCoverageContextFactory.Create(Game, State);
        _compatibility = TypeCoverageCompatibility.Evaluate(Dataset, _context);
        Recalculate();
    }

    /// <summary>
    /// Selects or unselects one hypothetical attacking type.
    /// </summary>
    /// <param name="type">The stable type identifier.</param>
    private void ToggleType(string type)
    {
        Selection.Toggle(type);
        Recalculate();
    }

    /// <summary>
    /// Restores the current player's damaging-move types.
    /// </summary>
    private void ResetToCurrentMoves()
    {
        Selection.ResetToCurrentMoves();
        Recalculate();
    }

    /// <summary>
    /// Recalculates aggregate results when compatible.
    /// </summary>
    private void Recalculate()
    {
        _calculation = _compatibility == TypeCoverageCompatibilityStatus.Compatible && _context is not null
            ? TypeCoverageCalculator.Calculate(Dataset, _context.Policy, Selection.SelectedTypes)
            : null;
    }

    /// <summary>
    /// Gets the visual classes for one type-selection button.
    /// </summary>
    /// <param name="type">The stable type identifier.</param>
    /// <returns>The button classes.</returns>
    private string GetTypeButtonClass(string type)
    {
        bool selected = Selection.SelectedTypes.Contains(type, StringComparer.Ordinal);
        string currentClass = IsCurrentMoveType(type)
            ? " current"
            : string.Empty;
        string color = selected ? $" {MovePresentation.GetTypeColorClass(type)}" : string.Empty;
        string selectedClass = selected ? " selected" : string.Empty;
        return $"coverage-type{currentClass}{color}{selectedClass}";
    }

    /// <summary>
    /// Gets whether one type is available from a current damaging move.
    /// </summary>
    /// <param name="type">The stable type identifier.</param>
    /// <returns><see langword="true"/> when a current damaging move has the type.</returns>
    private bool IsCurrentMoveType(string type)
        => Selection.CurrentMoveTypes.Contains(type, StringComparer.Ordinal);

    /// <summary>
    /// Gets the explanatory tooltip for one type-selection button.
    /// </summary>
    /// <param name="type">The stable type identifier.</param>
    /// <returns>The localized tooltip.</returns>
    private string GetTypeTitle(string type)
    {
        return IsCurrentMoveType(type)
            ? Text["Lookup.Coverage.CurrentType", type]
            : Text["Lookup.Coverage.HypotheticalType", type];
    }

    /// <summary>
    /// Gets a localized compatibility explanation.
    /// </summary>
    /// <returns>The compatibility message.</returns>
    private string GetCompatibilityMessage() => _compatibility switch
    {
        TypeCoverageCompatibilityStatus.MissingContext => Text["Lookup.Coverage.MissingContext"],
        TypeCoverageCompatibilityStatus.UnsupportedPolicy => Text["Lookup.Coverage.UnsupportedPolicy"],
        TypeCoverageCompatibilityStatus.GameVersionMismatch => Text["Lookup.Coverage.GameVersionMismatch"],
        TypeCoverageCompatibilityStatus.NormalPoolMismatch => Text["Lookup.Coverage.NormalPoolMismatch"],
        TypeCoverageCompatibilityStatus.FusionPoolMismatch => Text["Lookup.Coverage.FusionPoolMismatch"],
        _ => string.Empty
    };

    /// <summary>
    /// Gets one effectiveness bucket's compact multiplier label.
    /// </summary>
    /// <param name="effectiveness">The represented multiplier.</param>
    /// <returns>The multiplier label.</returns>
    private static string GetEffectivenessLabel(MoveEffectiveness effectiveness) => effectiveness switch
    {
        MoveEffectiveness.Immune => "0×",
        MoveEffectiveness.Quarter => "¼×",
        MoveEffectiveness.Half => "½×",
        MoveEffectiveness.Neutral => "1×",
        MoveEffectiveness.Double => "2×",
        MoveEffectiveness.Quadruple => "4×",
        _ => "—"
    };

    /// <summary>
    /// Gets one effectiveness bucket's visual class.
    /// </summary>
    /// <param name="effectiveness">The represented multiplier.</param>
    /// <returns>The bucket class.</returns>
    private static string GetBucketClass(MoveEffectiveness effectiveness) => effectiveness switch
    {
        MoveEffectiveness.Immune => "immune",
        MoveEffectiveness.Quarter or MoveEffectiveness.Half => "resisted",
        MoveEffectiveness.Neutral => "neutral",
        MoveEffectiveness.Double or MoveEffectiveness.Quadruple => "effective",
        _ => string.Empty
    };
}
