using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ironmon.Tracker.Collection.Achievements;

/// <summary>
/// Reads a small artwork index and decrypts one bounded embedded image at a time without retaining opened images.
/// </summary>
internal sealed class AchievementArtworkPack
{
    private const string _indexResource = "Ironmon.Tracker.Collection.Achievements.AchievementArtwork.index.pack";
    private const string _partResource = "Ironmon.Tracker.Collection.Achievements.AchievementArtwork.part-{0:D3}.pack";
    private const string _magic = "IRMART01";
    private const string _contextPrefix = "achievement-artwork-detail-v1:";
    private const string _key = "93E2B82EA8F9B85E1C1E98493D49BD586333D0A0737C5BD16B4D9E7C8628F239";
    private const string _pngPrefix = "data:image/png;base64,";
    private const int _resolution = 1024;
    private const int _maximumImageBytes = 8 * 1024 * 1024;
    private const int _maximumPartBytes = 32 * 1024 * 1024;
    private static readonly byte[] _pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly Lazy<AchievementArtworkPack> _shared = new(() => new(OpenResource));
    private readonly Func<string, Stream?> _openResource;
    private readonly Dictionary<Guid, ArtworkEntry> _entries = [];
    private readonly SemaphoreSlim _readGate = new(1, 1);

    /// <summary>
    /// Loads only opaque index entries, validating sizes before any image resource is opened.
    /// </summary>
    /// <param name="openResource">Opens an owned seekable resource stream, or returns null for a missing resource.</param>
    internal AchievementArtworkPack(Func<string, Stream?> openResource)
    {
        _openResource = openResource;
        using Stream stream = openResource(_indexResource) ?? throw new InvalidDataException("The achievement artwork index is missing.");
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
        if (Encoding.ASCII.GetString(ReadBytes(reader, 8)) != _magic || reader.ReadInt32() != 1)
            throw new InvalidDataException("The achievement artwork index version is invalid.");

        int count = reader.ReadInt32();
        if (count is < 1 or > 10000 || stream.Length != 16L + count * 68L)
            throw new InvalidDataException("The achievement artwork index exceeds its bounds.");

        for (int index = 0; index < count; index++)
        {
            Guid id = new(ReadBytes(reader, 16));
            ArtworkEntry entry = new(reader.ReadInt32(), reader.ReadInt64(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), ReadBytes(reader, 12), ReadBytes(reader, 16));
            if (id == Guid.Empty || entry.Part is < 0 or > 999 || entry.Offset < 0 || entry.Length is < 24 or > _maximumImageBytes || entry.Offset > _maximumPartBytes - entry.Length || entry.Width != _resolution || entry.Height != _resolution || !_entries.TryAdd(id, entry))
                throw new InvalidDataException("The achievement artwork index contains invalid entries.");
        }
    }

    /// <summary>
    /// Gets the shared immutable index without caching decrypted detail images.
    /// </summary>
    internal static AchievementArtworkPack Shared => _shared.Value;

    /// <summary>
    /// Checks whether an already earned icon has final detail artwork.
    /// </summary>
    /// <param name="iconId">The opaque image identity resolved from the existing thumbnail assignment.</param>
    /// <returns>Whether the index contains that image.</returns>
    internal bool Contains(Guid iconId)
        => _entries.ContainsKey(iconId);

    /// <summary>
    /// Reads one already authorized detail outside the collection writer with bounded concurrent decoding.
    /// </summary>
    /// <param name="iconId">The icon identity already verified as earned by the collection service.</param>
    /// <param name="cancellationToken">Cancels queued work or superseded image delivery.</param>
    /// <returns>The selected image, or null when final artwork is unavailable.</returns>
    internal async Task<AchievementArtwork?> ForEarnedAsync(Guid iconId, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(iconId, out ArtworkEntry? entry))
            return null;

        await _readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Open(iconId, entry, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _readGate.Release();
        }
    }

    /// <summary>
    /// Authenticates one PNG after seeking directly to its ciphertext and releases temporary buffers afterward.
    /// </summary>
    /// <param name="id">The authenticated image identity.</param>
    /// <param name="entry">The bounded part location and authentication metadata.</param>
    /// <param name="cancellationToken">Cancels a superseded read before its result is materialized.</param>
    /// <returns>The in-memory image for the selected viewer.</returns>
    private AchievementArtwork Open(Guid id, ArtworkEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string resource = string.Format(CultureInfo.InvariantCulture, _partResource, entry.Part);
        using Stream stream = _openResource(resource) ?? throw new InvalidDataException("The achievement artwork part is missing.");
        if (!stream.CanSeek || stream.Length > _maximumPartBytes || entry.Offset > stream.Length - entry.Length)
            throw new InvalidDataException("The achievement artwork part exceeds its bounds.");

        stream.Position = entry.Offset;
        byte[] ciphertext = new byte[entry.Length];
        stream.ReadExactly(ciphertext);
        cancellationToken.ThrowIfCancellationRequested();
        byte[] context = Encoding.UTF8.GetBytes(_contextPrefix + id);
        byte[] key = HMACSHA256.HashData(Convert.FromHexString(_key), context);
        byte[] plaintext = new byte[entry.Length];
        try
        {
            using AesGcm cipher = new(key, 16);
            cipher.Decrypt(entry.Nonce, ciphertext, entry.Tag, plaintext, context);
            if (!plaintext.AsSpan(0, 8).SequenceEqual(_pngSignature) || BinaryPrimitives.ReadInt32BigEndian(plaintext.AsSpan(16, 4)) != entry.Width || BinaryPrimitives.ReadInt32BigEndian(plaintext.AsSpan(20, 4)) != entry.Height)
                throw new InvalidDataException("The achievement artwork PNG dimensions are invalid.");

            cancellationToken.ThrowIfCancellationRequested();
            return new(entry.Width, entry.Height, _pngPrefix + Convert.ToBase64String(plaintext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Reads an exact fixed-size index field without accepting truncated metadata.
    /// </summary>
    /// <param name="reader">The index reader.</param>
    /// <param name="count">The required field length in bytes.</param>
    /// <returns>The complete field bytes.</returns>
    private static byte[] ReadBytes(BinaryReader reader, int count)
    {
        byte[] bytes = reader.ReadBytes(count);
        return bytes.Length == count ? bytes : throw new InvalidDataException("The achievement artwork index is truncated.");
    }

    /// <summary>
    /// Opens a resource from the collection assembly without extracting it to disk.
    /// </summary>
    /// <param name="name">The embedded resource identity.</param>
    /// <returns>An owned stream, or null when the resource is missing.</returns>
    private static Stream? OpenResource(string name)
        => typeof(AchievementArtworkPack).Assembly.GetManifestResourceStream(name);

    /// <summary>
    /// Locates one independently authenticated image inside a bounded artwork part.
    /// </summary>
    /// <remarks>
    /// Initializes only opaque offsets, dimensions, and authentication metadata.
    /// </remarks>
    /// <param name="Part">The numbered embedded resource.</param>
    /// <param name="Offset">The ciphertext's byte offset.</param>
    /// <param name="Length">The ciphertext length in bytes.</param>
    /// <param name="Width">The uniform pixel width.</param>
    /// <param name="Height">The uniform pixel height.</param>
    /// <param name="Nonce">The twelve-byte encryption nonce.</param>
    /// <param name="Tag">The sixteen-byte authentication tag.</param>
    private sealed record ArtworkEntry(int Part, long Offset, int Length, int Width, int Height, byte[] Nonce, byte[] Tag);
}
