namespace Ironmon.Tracker.Protocol.Wardrobe;

/// <summary>
/// Describes the authoritative game-owned wardrobe session and shared profile revision.
/// </summary>
public sealed record WardrobeSnapshotPayload
{
    /// <summary>
    /// Gets the supported wardrobe contract version.
    /// </summary>
    public int ProtocolVersion { get; init; }

    /// <summary>
    /// Gets the originating game-process session.
    /// </summary>
    public string GameSessionId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the game-issued wardrobe-session identity.
    /// </summary>
    public string WardrobeId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the accepted editing-session revision.
    /// </summary>
    public long Revision { get; init; }

    /// <summary>
    /// Gets the committed shared cosmetic-profile revision.
    /// </summary>
    public long ProfileRevision { get; init; }

    /// <summary>
    /// Gets the owning save-slot identity.
    /// </summary>
    public string AppearanceKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether the game is currently waiting in its wardrobe.
    /// </summary>
    public bool Open { get; init; }

    /// <summary>
    /// Gets whether this session ended by applying its appearance.
    /// </summary>
    public bool Confirmed { get; init; }

    /// <summary>
    /// Gets whether confirmation can claim the one-time free appearance.
    /// </summary>
    public bool InitialSelection { get; init; }

    /// <summary>
    /// Gets the shared spendable point balance.
    /// </summary>
    public int Points { get; init; }

    /// <summary>
    /// Gets the permanent cosmetic ownership keys.
    /// </summary>
    public IReadOnlyList<string> OwnedKeys { get; init; } = [];

    /// <summary>
    /// Gets the accepted draft appearance.
    /// </summary>
    public WardrobeAppearancePayload Appearance { get; init; } = new();

    /// <summary>
    /// Gets the actual equipped appearance.
    /// </summary>
    public WardrobeAppearancePayload EquippedAppearance { get; init; } = new();

    /// <summary>
    /// Gets the saved profile currently associated with this draft, or null.
    /// </summary>
    public string? LoadedProfileId { get; init; }

    /// <summary>
    /// Gets the named outfits shared across save slots.
    /// </summary>
    public IReadOnlyList<WardrobeOutfitProfilePayload> OutfitProfiles { get; init; } = [];

    /// <summary>
    /// Gets the complete catalog on opening or recovery, or null on an ordinary edit.
    /// </summary>
    public IReadOnlyList<WardrobeCatalogItemPayload>? Catalog { get; init; }
}
