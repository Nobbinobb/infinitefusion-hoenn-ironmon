namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Supplies optional tracker-indexed authored trainer calls without exposing generated trainer parties.
/// </summary>
public sealed class TrainerLocationIndexAssignmentPayload
{
    /// <summary>
    /// Gets the supported assignment schema version.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Gets or initializes the assignment schema version.
    /// </summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>
    /// Gets or initializes the local game installation whose authored map files were indexed.
    /// </summary>
    public required string GameRoot { get; init; }

    /// <summary>
    /// Gets or initializes the connection-scoped identity acknowledged by the game after installation.
    /// </summary>
    public required string ContextToken { get; init; }

    /// <summary>
    /// Gets or initializes ordered map identifiers keyed by the base64-encoded authored trainer type and name separated by a null character.
    /// </summary>
    /// <remarks>
    /// Null entries ask the game to reuse an acknowledged index. The game resolves route labels and
    /// quest-map exclusions, and retains its original location lookup when no index is available.
    /// Encoding the byte keys preserves null separators through the game's JSON object-key decoder.
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<int>>? Locations { get; init; }
}
