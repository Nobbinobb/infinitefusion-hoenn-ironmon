namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents the combined uses of one item across all recorded consumption sources.
/// </summary>
public sealed class ArchivedItemUsage
{
    /// <summary>
    /// Gets or initializes the item display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets or initializes its presentation category, or null for an unknown catalog entry.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Gets or initializes the combined number of uses.
    /// </summary>
    public long Count { get; init; }
}
