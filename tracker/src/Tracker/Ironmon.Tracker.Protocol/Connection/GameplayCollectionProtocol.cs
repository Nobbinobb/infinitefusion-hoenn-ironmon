namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Names and bounds for the independent gameplay collection channel.
/// </summary>
public static class GameplayCollectionProtocol
{
    /// <summary>
    /// The optional earned-achievement notification channel version.
    /// </summary>
    public const int AchievementVersion = 2;

    /// <summary>
    /// Requests a presentation-only notification without changing the unlock ledger.
    /// </summary>
    public const string AchievementPreview = "achievement_preview";

    /// <summary>
    /// Limits image-bearing notification batches well below the one-MiB transport frame.
    /// </summary>
    public const int MaximumAchievementNotices = 8;

    /// <summary>
    /// Requests earned notices still awaiting display.
    /// </summary>
    public const string AchievementPoll = "achievement_poll";

    /// <summary>
    /// Delivers only earned identities, titles and bounded icon pixels to the game.
    /// </summary>
    public const string AchievementNotifications = "achievement_unlocks";

    /// <summary>
    /// Acknowledges stable identities after actual game display.
    /// </summary>
    public const string AchievementAcknowledged = "achievement_displayed";
    /// <summary>
    /// The supported protocol version.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// A complete collection batch.
    /// </summary>
    public const string Batch = "collection_batch";

    /// <summary>
    /// A bounded zlib-compressed JSON batch with an exact original length and digest.
    /// </summary>
    public const string CompressedBatch = "collection_compressed_batch";

    /// <summary>
    /// The optional compressed batch format supported alongside ordinary protocol version 1 messages.
    /// </summary>
    public const int CompressionVersion = 1;

    /// <summary>
    /// A physical fragment of an oversized batch.
    /// </summary>
    public const string Chunk = "collection_chunk";

    /// <summary>
    /// A prefix committed to the database.
    /// </summary>
    public const string Acknowledged = "collection_acknowledged";

    /// <summary>
    /// An in-memory transport fragment receipt, which never acknowledges gameplay.
    /// </summary>
    public const string ChunkAcknowledged = "collection_chunk_acknowledged";

    /// <summary>
    /// A rejected transfer whose source data must remain pending.
    /// </summary>
    public const string Rejected = "collection_rejected";

    /// <summary>
    /// Operational collection health without gameplay payloads.
    /// </summary>
    public const string Status = "collection_status";

    /// <summary>
    /// The collection directory beneath the tracker profile.
    /// </summary>
    public const string DirectoryName = "collection-v1";

    /// <summary>
    /// The largest assembled batch accepted by this protocol.
    /// </summary>
    public const int MaximumBatchBytes = 16 * 1024 * 1024;

    /// <summary>
    /// The largest decoded physical fragment.
    /// </summary>
    public const int MaximumChunkBytes = 128 * 1024;
}
