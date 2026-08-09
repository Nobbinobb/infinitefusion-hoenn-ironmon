namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Defines stable identifiers used by completed-run evolution metrics.
/// </summary>
public static class EvolutionMetricIdentifiers
{
    /// <summary>
    /// Gets the supported metrics schema version.
    /// </summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// Gets the outcome used before an offered evolution resolves.
    /// </summary>
    public const string OfferedOutcome = "offered";

    /// <summary>
    /// Gets the successful evolution outcome.
    /// </summary>
    public const string CompletedOutcome = "completed";

    /// <summary>
    /// Gets the player-cancelled evolution outcome.
    /// </summary>
    public const string CancelledOutcome = "cancelled";
}
