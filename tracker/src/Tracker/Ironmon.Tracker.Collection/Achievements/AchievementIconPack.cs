using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ironmon.Tracker.Collection.Achievements;

/// <summary>
/// Lazily opens independently encrypted artwork without exposing source filenames or loose PNGs.
/// </summary>
internal sealed class AchievementIconPack
{
    private const string _resource = "Ironmon.Tracker.Collection.AchievementIcons.pack";
    private const string _contextPrefix = "achievement-icons-v1:";
    private const string _key = "93E2B82EA8F9B85E1C1E98493D49BD586333D0A0737C5BD16B4D9E7C8628F239";
    private const string _pngPrefix = "data:image/png;base64,";
    private static readonly Lazy<AchievementIconPack> _shared = new(() => new());
    private readonly Lock _sync = new();
    private readonly IconCatalogue _catalogue;
    private readonly Dictionary<Guid, SealedIcon> _sealed;
    private readonly Dictionary<Guid, AchievementIcon> _opened = [];

    /// <summary>
    /// Loads ciphertext and opaque mappings while leaving every image encrypted.
    /// </summary>
    private AchievementIconPack()
    {
        using Stream stream = typeof(AchievementIconPack).Assembly.GetManifestResourceStream(_resource) ?? throw new InvalidDataException("The achievement icon pack is missing.");
        _catalogue = JsonSerializer.Deserialize<IconCatalogue>(stream) ?? throw new InvalidDataException("The achievement icon pack is invalid.");
        _sealed = _catalogue.Icons.ToDictionary(icon => icon.Id);
        if (_catalogue.Version != 1 || !_sealed.ContainsKey(_catalogue.DefaultId) || _catalogue.Assignments.Values.Any(id => !_sealed.ContainsKey(id)))
            throw new InvalidDataException("The achievement icon pack has invalid identities.");
    }

    /// <summary>
    /// Gets the process-wide, lazily initialized artwork catalogue.
    /// </summary>
    internal static AchievementIconPack Shared => _shared.Value;

    /// <summary>
    /// Gets the explicitly previewable shared test artwork without reading achievement rules or progress.
    /// </summary>
    internal AchievementIcon Preview => Open(_catalogue.DefaultId);

    /// <summary>
    /// Resolves artwork for an identity already verified as earned by the collection service.
    /// </summary>
    /// <param name="achievementId">The authenticated earned identity.</param>
    /// <returns>The assigned artwork, or the shared test image while final icons are unfinished.</returns>
    internal AchievementIcon ForEarned(Guid achievementId)
        => Open(_catalogue.Assignments.GetValueOrDefault(achievementId, _catalogue.DefaultId));

    /// <summary>
    /// Authenticates one icon and expands its bounded pixel buffer only when requested.
    /// </summary>
    /// <param name="id">The opaque artwork identity.</param>
    /// <returns>Cached immutable image strings for in-memory display.</returns>
    private AchievementIcon Open(Guid id)
    {
        lock (_sync)
        {
            if (_opened.TryGetValue(id, out AchievementIcon? cached))
                return cached;

            SealedIcon icon = _sealed[id];
            byte[] context = Encoding.UTF8.GetBytes(_contextPrefix + id);
            byte[] key = HMACSHA256.HashData(Convert.FromHexString(_key), context);
            byte[] plaintext = new byte[icon.Ciphertext.Length];
            try
            {
                using AesGcm cipher = new(key, 16);
                cipher.Decrypt(icon.Nonce, icon.Ciphertext, icon.Tag, plaintext, context);
                IconContent content = JsonSerializer.Deserialize<IconContent>(plaintext) ?? throw new InvalidDataException("The achievement icon is invalid.");
                if (content.Width is < 1 or > 64 || content.Height is < 1 or > 64 || content.Png.Length is < 8 or > 65536)
                    throw new InvalidDataException("The achievement icon exceeds its image bounds.");

                byte[] pixels = new byte[content.Width * content.Height * 4];
                using MemoryStream input = new(content.Pixels, false);
                using ZLibStream inflater = new(input, CompressionMode.Decompress);
                inflater.ReadExactly(pixels);
                if (inflater.ReadByte() != -1)
                    throw new InvalidDataException("The achievement icon has an invalid pixel count.");

                AchievementIcon opened = new(content.Width, content.Height, Convert.ToBase64String(pixels), _pngPrefix + Convert.ToBase64String(content.Png));
                _opened.Add(id, opened);
                return opened;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }

    /// <summary>
    /// Describes the encrypted pack envelope.
    /// </summary>
    /// <remarks>
    /// Initializes only opaque mappings and ciphertext.
    /// </remarks>
    /// <param name="Version">The artwork format version.</param>
    /// <param name="DefaultId">The shared test artwork identity.</param>
    /// <param name="Assignments">Stable earned identities mapped to artwork identities.</param>
    /// <param name="Icons">Independently authenticated encrypted images.</param>
    private sealed record IconCatalogue(int Version, Guid DefaultId, Dictionary<Guid, Guid> Assignments, SealedIcon[] Icons);

    /// <summary>
    /// Retains one image without decrypting it at catalogue load time.
    /// </summary>
    /// <remarks>
    /// Initializes an authenticated ciphertext envelope.
    /// </remarks>
    /// <param name="Id">The opaque artwork identity.</param>
    /// <param name="Nonce">The twelve-byte nonce.</param>
    /// <param name="Tag">The sixteen-byte authentication tag.</param>
    /// <param name="Ciphertext">Encrypted PNG and compressed RGBA data.</param>
    private sealed record SealedIcon(Guid Id, byte[] Nonce, byte[] Tag, byte[] Ciphertext);

    /// <summary>
    /// Describes one decrypted image inside the authenticated envelope.
    /// </summary>
    /// <remarks>
    /// Initializes the two render representations produced by the private build tool.
    /// </remarks>
    /// <param name="Width">The image width in pixels.</param>
    /// <param name="Height">The image height in pixels.</param>
    /// <param name="Png">PNG bytes used by the tracker without writing a file.</param>
    /// <param name="Pixels">Zlib-compressed RGBA8 bytes used by the game.</param>
    private sealed record IconContent(int Width, int Height, byte[] Png, byte[] Pixels);
}
