namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Describes one possible evolution requirement without revealing its destination.
/// </summary>
public sealed class EvolutionSnapshot
{
    /// <summary>
    /// Initializes an empty evolution snapshot for protocol serialization.
    /// </summary>
    public EvolutionSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the compact requirement kind.
    /// </summary>
    public EvolutionRequirementKind Kind { get; init; }

    /// <summary>
    /// Gets or initializes the required level when applicable.
    /// </summary>
    public int? Level { get; init; }

    /// <summary>
    /// Gets or initializes the stable required item identifier when applicable.
    /// </summary>
    public string? ItemId { get; init; }

    /// <summary>
    /// Gets or initializes the localized required item name when applicable.
    /// </summary>
    public string? ItemName { get; init; }

    /// <summary>
    /// Gets or initializes the localized compact requirement text.
    /// </summary>
    public required string Requirement { get; init; }
}
