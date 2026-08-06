using Ironmon.Tracker.Protocol;

namespace Ironmon.Tracker.Connection;

/// <summary>
/// Represents the compact tracker-owned state persisted for one run.
/// </summary>
internal sealed class PersistedRunKnowledge
{
    /// <summary>
    /// Initializes empty persisted run knowledge.
    /// </summary>
    public PersistedRunKnowledge()
    {
    }

    /// <summary>
    /// Gets or initializes move discoveries keyed by species and form.
    /// </summary>
    public Dictionary<string, List<ObservedMoveSnapshot>> Moves { get; init; } = [];

    /// <summary>
    /// Gets or initializes stat annotations keyed by species, form, and stat.
    /// </summary>
    public Dictionary<string, Dictionary<string, EnemyStatAnnotation>> Annotations { get; init; } = [];
}
