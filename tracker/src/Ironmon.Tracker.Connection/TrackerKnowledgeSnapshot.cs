using Ironmon.Tracker.Protocol;

namespace Ironmon.Tracker.Connection;

/// <summary>
/// Represents an immutable diagnostic copy of tracker-owned run knowledge.
/// </summary>
/// <remarks>
/// Initializes one tracker knowledge snapshot.
/// </remarks>
/// <param name="runId">The selected run identifier.</param>
/// <param name="moves">Move discoveries keyed by species and form.</param>
/// <param name="abilities">Ability discoveries keyed by species and form.</param>
/// <param name="highestLevels">Highest encountered levels keyed by species and form.</param>
/// <param name="annotations">Manual stat annotations keyed by species, form, and stat.</param>
/// <param name="lastError">The most recent persistence error.</param>
public sealed class TrackerKnowledgeSnapshot(string? runId, IReadOnlyDictionary<string, IReadOnlyList<ObservedMoveSnapshot>> moves, IReadOnlyDictionary<string, IReadOnlyList<AbilitySnapshot>> abilities, IReadOnlyDictionary<string, int> highestLevels, IReadOnlyDictionary<string, IReadOnlyDictionary<string, EnemyStatAnnotation>> annotations, string? lastError)
{
    /// <summary>
    /// Gets the selected run identifier.
    /// </summary>
    public string? RunId { get; } = runId;

    /// <summary>
    /// Gets move discoveries keyed by species and form.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ObservedMoveSnapshot>> Moves { get; } = moves;

    /// <summary>
    /// Gets ability discoveries keyed by species and form.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<AbilitySnapshot>> Abilities { get; } = abilities;

    /// <summary>
    /// Gets highest encountered levels keyed by species and form.
    /// </summary>
    public IReadOnlyDictionary<string, int> HighestLevels { get; } = highestLevels;

    /// <summary>
    /// Gets manual stat annotations keyed by species, form, and stat.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, EnemyStatAnnotation>> Annotations { get; } = annotations;

    /// <summary>
    /// Gets the most recent persistence error.
    /// </summary>
    public string? LastError { get; } = lastError;
}
