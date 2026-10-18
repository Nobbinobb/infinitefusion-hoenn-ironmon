using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Maps the existing opaque encrypted consumer checkpoint.
/// </summary>
[SqlTable(_table)]
internal sealed class AchievementStateRow
{
    private const string _table = "private_consumers";
    private const string _key = "consumer_key";
    private const string _binding = "pack_binding_token";
    private const string _stateRevision = "state_revision";
    private const string _through = "evaluated_through_revision";
    private const string _format = "cipher_format_version";
    private const string _slot = "key_slot";
    private const string _nonce = "nonce";
    private const string _tag = "auth_tag";
    private const string _ciphertext = "ciphertext";

    /// <summary>
    /// Gets or initializes the sixteen-byte opaque consumer identity.
    /// </summary>
    [SqlColumn(_key)]
    public byte[] Key { get; init; } = [];

    /// <summary>
    /// Gets or initializes the thirty-two-byte definition-pack digest authenticated with this checkpoint.
    /// </summary>
    [SqlColumn(_binding)]
    public byte[] Binding { get; init; } = [];

    /// <summary>
    /// Gets or initializes the monotonic private-checkpoint revision.
    /// </summary>
    [SqlColumn(_stateRevision)]
    public long StateRevision { get; init; }

    /// <summary>
    /// Gets or initializes the eight-byte collection revision evaluated by this checkpoint.
    /// </summary>
    [SqlColumn(_through)]
    public byte[] Through { get; init; } = [];

    /// <summary>
    /// Gets or initializes the encrypted-checkpoint format version.
    /// </summary>
    [SqlColumn(_format)]
    public int Format { get; init; }

    /// <summary>
    /// Gets or initializes the encryption-key slot identifier.
    /// </summary>
    [SqlColumn(_slot)]
    public int Slot { get; init; }

    /// <summary>
    /// Gets or initializes the twelve-byte AES-GCM nonce.
    /// </summary>
    [SqlColumn(_nonce)]
    public byte[] Nonce { get; init; } = [];

    /// <summary>
    /// Gets or initializes the sixteen-byte AES-GCM authentication tag.
    /// </summary>
    [SqlColumn(_tag)]
    public byte[] Tag { get; init; } = [];

    /// <summary>
    /// Gets or initializes the encrypted unlock ledger and pending notification identities.
    /// </summary>
    [SqlColumn(_ciphertext)]
    public byte[] Ciphertext { get; init; } = [];
}
