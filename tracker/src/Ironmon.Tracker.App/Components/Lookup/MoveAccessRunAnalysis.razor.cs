using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents move-access iteration data only after a run has completed.
/// </summary>
public partial class MoveAccessRunAnalysis
{
    /// <summary>
    /// Gets or sets the completed run's observed move-access metrics.
    /// </summary>
    [Parameter]
    public MoveAccessMetricsPayload? Metrics { get; set; }

    /// <summary>
    /// Formats a stable side identifier.
    /// </summary>
    /// <param name="side">The side identifier.</param>
    /// <returns>The localized side label.</returns>
    private string FormatSide(string side) => side switch
    {
        MoveAccessMetricIdentifiers.PlayerSide => Text["Lookup.Analysis.Player"],
        MoveAccessMetricIdentifiers.EnemySide => Text["Lookup.Analysis.Enemy"],
        _ => side
    };

    /// <summary>
    /// Formats a stable access-channel identifier.
    /// </summary>
    /// <param name="channel">The channel identifier.</param>
    /// <returns>The localized channel label.</returns>
    private string FormatChannel(string channel) => channel switch
    {
        MoveAccessMetricIdentifiers.LearnsetChannel => Text["Lookup.Analysis.Learnset"],
        MoveAccessMetricIdentifiers.EggChannel => Text["Lookup.Analysis.Egg"],
        MoveAccessMetricIdentifiers.MachineChannel => Text["Lookup.Analysis.Machine"],
        MoveAccessMetricIdentifiers.TutorChannel => Text["Lookup.Analysis.Tutor"],
        _ => channel
    };

    /// <summary>
    /// Formats a stable metric source identifier for display.
    /// </summary>
    /// <param name="source">The source identifier.</param>
    /// <returns>A readable localized source.</returns>
    private string FormatSource(string source)
        => Text[$"Lookup.Analysis.Source.{source}"];

    /// <summary>
    /// Formats an optional metric count.
    /// </summary>
    /// <param name="value">The optional value.</param>
    /// <returns>The value or an em dash.</returns>
    private static string FormatOptionalNumber(int? value)
        => value?.ToString(CultureInfo.CurrentCulture) ?? "—";

    /// <summary>
    /// Formats optional growth with an explicit positive sign.
    /// </summary>
    /// <param name="value">The optional growth value.</param>
    /// <returns>The signed value or an em dash.</returns>
    private static string FormatSignedNumber(int? value)
        => value is null ? "—" : value.Value.ToString("+#;-#;0", CultureInfo.CurrentCulture);

    /// <summary>
    /// Formats the earliest damaging-move level.
    /// </summary>
    /// <param name="level">The optional level.</param>
    /// <returns>The localized fact.</returns>
    private string FormatEarliestDamage(int? level)
        => level is null ? Text["Lookup.Analysis.NoDamagingMove"] : Text["Lookup.Analysis.EarliestDamage", level.Value];

    /// <summary>
    /// Formats the theoretical initial move composition.
    /// </summary>
    /// <param name="moves">The initial move metrics.</param>
    /// <returns>A concise comma-separated list.</returns>
    private string FormatInitialMoves(IReadOnlyList<MoveAccessInitialMoveMetricPayload> moves)
        => moves.Count == 0 ? Text["Lookup.Analysis.NoneRecorded"] : string.Join(", ", moves.Select(move => $"{move.MoveName} ({FormatCategory(move.Category)})"));

    /// <summary>
    /// Formats one move category.
    /// </summary>
    /// <param name="category">The category identifier.</param>
    /// <returns>The localized category.</returns>
    private string FormatCategory(string category)
    {
        if (!Enum.TryParse(category, true, out MoveCategory parsed))
            parsed = MoveCategory.Unknown;

        return parsed switch
        {
            MoveCategory.Physical => Text["Common.Move.Physical"],
            MoveCategory.Special => Text["Common.Move.Special"],
            MoveCategory.Status => Text["Common.Move.Status"],
            _ => Text["Common.Move.Unknown"]
        };
    }

    /// <summary>
    /// Formats the result of a tutor visit.
    /// </summary>
    /// <param name="speciesName">The taught Pokemon species name.</param>
    /// <returns>The localized taught-state text.</returns>
    private string FormatTaughtPokemon(string? speciesName)
        => speciesName is null ? Text["Lookup.Analysis.NothingTaught"] : Text["Lookup.Analysis.TaughtPokemon", speciesName];
}
