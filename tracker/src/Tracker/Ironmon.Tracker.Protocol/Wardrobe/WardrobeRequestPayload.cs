namespace Ironmon.Tracker.Protocol.Wardrobe;

/// <summary>
/// Carries a request scoped to an already open game-issued wardrobe session.
/// </summary>
public sealed record WardrobeRequestPayload
{
    /// <summary>
    /// Gets the originating game-process identity.
    /// </summary>
    public string GameSessionId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the game-issued editing-session identity.
    /// </summary>
    public string WardrobeId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the editing revision this request expects.
    /// </summary>
    public long Revision { get; init; }

    /// <summary>
    /// Gets the shared profile revision observed by the caller.
    /// </summary>
    public long ProfileRevision { get; init; }

    /// <summary>
    /// Gets the stable mutation identity used to deduplicate retransmissions, or null for previews.
    /// </summary>
    public string? OperationId { get; init; }

    /// <summary>
    /// Gets the requested operation; no operation opens the wardrobe.
    /// </summary>
    public WardrobeAction Action { get; init; }

    /// <summary>
    /// Gets the complete replacement draft for an appearance change.
    /// </summary>
    public WardrobeAppearancePayload? Appearance { get; init; }

    /// <summary>
    /// Gets the selected cosmetic key for purchase or thumbnail rendering.
    /// </summary>
    public string? PieceKey { get; init; }

    /// <summary>
    /// Gets the saved outfit identity for profile operations.
    /// </summary>
    public string? ProfileId { get; init; }

    /// <summary>
    /// Gets the requested saved-outfit name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the native portrait, front, back, left, right, or bicycle preview mode.
    /// </summary>
    public string? Mode { get; init; }

    /// <summary>
    /// Gets hat or hat2 when rendering an accessory candidate.
    /// </summary>
    public string? AccessorySlot { get; init; }

    /// <summary>
    /// Gets whether a portrait thumbnail focuses on the head.
    /// </summary>
    public bool HeadOnly { get; init; }

    /// <summary>
    /// Gets the native dye field whose available swatches should be returned.
    /// </summary>
    public string? DyeField { get; init; }
}
