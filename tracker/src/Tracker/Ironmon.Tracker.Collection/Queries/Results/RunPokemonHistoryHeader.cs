namespace Ironmon.Tracker.Collection.Queries.Results;

/// <summary>
/// Reads ordered runner configuration headers without expanding every historical payload.
/// </summary>
internal sealed class RunPokemonHistoryHeader
{
    /// <summary>
    /// Gets or initializes the exact load branch containing this configuration.
    /// </summary>
    public long Branch { get; init; }
    /// <summary>
    /// Gets or initializes the producer stream used by saved history cursors.
    /// </summary>
    public byte[] Stream { get; init; } = [];
    /// <summary>
    /// Gets or initializes the retained individual identity.
    /// </summary>
    public long Individual { get; init; }
    /// <summary>
    /// Gets or initializes the interned species identity.
    /// </summary>
    public long Species { get; init; }
    /// <summary>
    /// Gets or initializes the interned form identity.
    /// </summary>
    public long? Form { get; init; }
    /// <summary>
    /// Gets or initializes the actual recorded level.
    /// </summary>
    public long Level { get; init; }
    /// <summary>
    /// Gets or initializes the global ordered storage revision.
    /// </summary>
    public byte[] Recorded { get; init; } = [];
    /// <summary>
    /// Gets or initializes the ordered source event sequence within that revision.
    /// </summary>
    public byte[] Sequence { get; init; } = [];
    /// <summary>
    /// Gets or initializes the immutable complete configuration payload identity.
    /// </summary>
    public long Detail { get; init; }
}
