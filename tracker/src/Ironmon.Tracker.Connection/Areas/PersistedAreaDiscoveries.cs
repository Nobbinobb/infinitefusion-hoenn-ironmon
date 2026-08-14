namespace Ironmon.Tracker.Connection.Areas;

/// <summary>
/// Represents tracker-owned area discoveries and complete disclosed entries for one run.
/// </summary>
internal sealed class PersistedAreaDiscoveries
{
    /// <summary>
    /// Initializes empty persisted area discoveries.
    /// </summary>
    public PersistedAreaDiscoveries()
    {
    }

    /// <summary>
    /// Gets or initializes the persistence schema version.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// Gets or sets the monotonic tracker-owned revision.
    /// </summary>
    public long Revision { get; set; }

    /// <summary>
    /// Gets or initializes discovery keys grouped by area and category.
    /// </summary>
    public Dictionary<string, Dictionary<string, HashSet<string>>> Discoveries { get; init; } = [];

    /// <summary>
    /// Gets or initializes complete disclosed trainer entries keyed by entry identifier.
    /// </summary>
    public Dictionary<string, AreaTrainerEntryPayload> Trainers { get; init; } = [];

    /// <summary>
    /// Gets or initializes complete disclosed encounter entries keyed by entry identifier.
    /// </summary>
    public Dictionary<string, AreaEncounterEntryPayload> Encounters { get; init; } = [];

    /// <summary>
    /// Gets or initializes complete disclosed item entries keyed by entry identifier.
    /// </summary>
    public Dictionary<string, AreaItemEntryPayload> Items { get; init; } = [];
}
