using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents locally observed generated-evolution outcomes for a completed run.
/// </summary>
public partial class EvolutionRunAnalysis
{
    /// <summary>
    /// Gets or sets the completed-run evolution metrics.
    /// </summary>
    [Parameter]
    public EvolutionMetricsPayload? Metrics { get; set; }

    /// <summary>
    /// Counts events with one outcome.
    /// </summary>
    /// <param name="outcome">The stable outcome identifier.</param>
    /// <returns>The matching event count.</returns>
    private int CountOutcome(string outcome)
        => Metrics?.Events.Count(metric => metric.Outcome == outcome) ?? 0;

    /// <summary>
    /// Formats the level, source kind, and optional fusion side.
    /// </summary>
    /// <param name="metric">The represented evolution event.</param>
    /// <returns>The compact event context.</returns>
    private string FormatContext(EvolutionMetricPayload metric)
    {
        return metric.ComponentSide is null
            ? Text["Lookup.EvolutionAnalysis.Context", metric.Level, metric.SourceKind]
            : Text["Lookup.EvolutionAnalysis.FusionContext", metric.Level, metric.ComponentSide];
    }

    /// <summary>
    /// Formats an evolution trigger.
    /// </summary>
    /// <param name="metric">The represented evolution event.</param>
    /// <returns>The effective method and optional parameter.</returns>
    private string FormatTrigger(EvolutionMetricPayload metric)
    {
        if (metric.Forced)
            return Text["Lookup.EvolutionAnalysis.Forced"];

        if (metric.EffectiveMethod is null)
            return metric.ActivationContext;

        return metric.EffectiveParameter is null
            ? metric.EffectiveMethod
            : Text["Lookup.EvolutionAnalysis.MethodParameter", metric.EffectiveMethod, metric.EffectiveParameter];
    }

    /// <summary>
    /// Formats a stable evolution outcome.
    /// </summary>
    /// <param name="outcome">The stable outcome identifier.</param>
    /// <returns>The displayed outcome.</returns>
    private string FormatOutcome(string outcome) => outcome switch
    {
        EvolutionMetricIdentifiers.CompletedOutcome => Text["Lookup.EvolutionAnalysis.Completed"],
        EvolutionMetricIdentifiers.CancelledOutcome => Text["Lookup.EvolutionAnalysis.Cancelled"],
        _ => Text["Lookup.EvolutionAnalysis.Offered"]
    };
}
