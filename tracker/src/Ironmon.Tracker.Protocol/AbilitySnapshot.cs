namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Describes one legally known ability and its display details.
/// </summary>
public sealed class AbilitySnapshot
{
    /// <summary>
    /// Initializes an empty ability snapshot for protocol serialization.
    /// </summary>
    public AbilitySnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the stable ability identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets or initializes the localized ability name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets or initializes the localized ability description.
    /// </summary>
    public required string Description { get; init; }
}
