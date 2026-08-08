namespace Ironmon.Tracker.Protocol.Debug;

/// <summary>
/// Describes one current, generated, component, or final fusion ability slot.
/// </summary>
public sealed class DebugAbilitySlotSnapshot
{
    /// <summary>
    /// Initializes an empty debug ability slot for protocol serialization.
    /// </summary>
    public DebugAbilitySlotSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the inspector section containing the slot.
    /// </summary>
    public DebugAbilitySlotGroup Group { get; init; }

    /// <summary>
    /// Gets or initializes the normal or hidden slot kind.
    /// </summary>
    public DebugAbilitySlotKind Kind { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based slot position within its kind.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Gets or initializes the generated ability identifier.
    /// </summary>
    public required string AbilityId { get; init; }

    /// <summary>
    /// Gets or initializes the localized generated ability name.
    /// </summary>
    public required string AbilityName { get; init; }

    /// <summary>
    /// Gets or initializes the original ability identifier for the same slot.
    /// </summary>
    public string? OriginalAbilityId { get; init; }

    /// <summary>
    /// Gets or initializes the localized original ability name for the same slot.
    /// </summary>
    public string? OriginalAbilityName { get; init; }

    /// <summary>
    /// Gets or initializes the randomizer eligibility classification.
    /// </summary>
    public DebugAbilityEligibility Eligibility { get; init; }

    /// <summary>
    /// Gets or initializes whether this entry is the Pokemon's active ability.
    /// </summary>
    public bool Active { get; init; }

    /// <summary>
    /// Gets or initializes the fusion component and source-slot description.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Gets or initializes the source ability identifier for a final fusion slot.
    /// </summary>
    public string? SourceAbilityId { get; init; }

    /// <summary>
    /// Gets or initializes the localized source ability name for a final fusion slot.
    /// </summary>
    public string? SourceAbilityName { get; init; }

    /// <summary>
    /// Gets or initializes whether a restricted source was replaced in the final fusion slot.
    /// </summary>
    public bool RestrictedSourceReplaced { get; init; }
}
