using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Shares source reference components and stores hexadecimal native strings as exact binary bytes.
/// </summary>
internal sealed partial class CollectionValueStore
{
    private const byte _nativeStringMarker = 255;
    private const byte _lowerHexString = 1;
    private const byte _upperHexString = 2;
    private const byte _sourceReferenceString = 3;

    /// <summary>
    /// Enables compact string writes only after the old dictionary has been upgraded successfully.
    /// </summary>
    public void EnableCompactStrings() => _compactNativeStrings = true;

    /// <summary>
    /// Rewrites native strings in place while preserving all existing value IDs and structured references.
    /// </summary>
    public void CompactStoredStrings()
    {
        List<(long Id, string Text)> rows = [];
        Dictionary<string, long> identities = new(StringComparer.Ordinal);
        foreach (CollectionStringRecord stored in CollectionQueries.FindStoredStrings(connection, transaction()))
        {
            byte[] payload = stored.Payload;
            if (!SHA256.HashData(payload).AsSpan(0, 8).SequenceEqual(stored.Digest))
                throw new InvalidDataException("A native string failed its pre-migration integrity check.");

            string text = DecodeNativeString(payload, 0);
            rows.Add((stored.Id, text));
            identities.TryAdd(text, stored.Id);
        }

        for (int index = 0; index < rows.Count; index++)
        {
            var (id, text) = rows[index];
            byte[] payload = EncodeNativeString(text, component => InternMigrationString(component, identities, rows));
            CollectionQueries.SaveNativeString(connection, transaction(), id, payload, SHA256.HashData(payload)[..8]);
        }

        ClearCaches();
        _compactNativeStrings = true;
    }

    /// <summary>
    /// Reuses an existing string ID during migration before any representation-dependent lookup changes.
    /// </summary>
    /// <param name="text">The exact component string.</param>
    /// <param name="identities">The complete semantic string-to-ID mapping for this migration.</param>
    /// <param name="pending">The remaining string bodies, including newly added components.</param>
    /// <returns>The preserved or newly allocated immutable string ID.</returns>
    private long InternMigrationString(string text, Dictionary<string, long> identities, List<(long Id, string Text)> pending)
    {
        if (identities.TryGetValue(text, out long existing))
            return existing;

        byte[] payload = Encoding.UTF8.GetBytes(text);
        long id = CollectionQueries.InsertValue(connection, transaction(), (int)ValueKind.String, payload, SHA256.HashData(payload)[..8]);
        identities[text] = id;
        pending.Add((id, text));
        return id;
    }

    /// <summary>
    /// Encodes a native string while preserving its logical string type, spelling and case.
    /// </summary>
    /// <param name="text">The complete original string.</param>
    /// <param name="resolve">Resolves a shared source-prefix or kind string to its immutable ID.</param>
    /// <returns>Exact compact bytes, or the original UTF-8 representation when no supported reduction applies.</returns>
    private static byte[] EncodeNativeString(string text, Func<string, long> resolve)
    {
        bool lowerHex = text.Length >= 16 && text.Length % 2 == 0 && text.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
        bool upperHex = text.Length >= 16 && text.Length % 2 == 0 && text.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
        if (lowerHex || upperHex)
            return [_nativeStringMarker, lowerHex ? _lowerHexString : _upperHexString, .. Convert.FromHexString(text)];

        int separator = text.Length > 33 && text[32] == ':' ? text.IndexOf(':', 33) : -1;
        if (separator > 33 && separator + 1 < text.Length
            && text[..32].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            && text[33..separator].All(character => character is >= 'a' and <= 'z' or '_')
            && text[(separator + 1)..].All(character => character is >= '0' and <= '9')
            && (text[separator + 1] != '0' || separator + 2 == text.Length))
        {
            using MemoryStream output = new();
            output.WriteByte(_nativeStringMarker);
            output.WriteByte(_sourceReferenceString);
            WriteUnsigned(output, resolve(text[..32]));
            WriteUnsigned(output, resolve(text[33..separator]));
            output.Write(CollectionEncoding.Unsigned(BigInteger.Parse(text.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture)));
            byte[] compact = output.ToArray();
            if (compact.Length < Encoding.UTF8.GetByteCount(text))
                return compact;
        }

        return Encoding.UTF8.GetBytes(text);
    }

    /// <summary>
    /// Restores a compact native string without interpreting a source counter as a JSON number.
    /// </summary>
    /// <param name="payload">The exact stored string body.</param>
    /// <param name="depth">The current value dependency depth.</param>
    /// <returns>The complete original string.</returns>
    private string DecodeNativeString(byte[] payload, int depth)
    {
        GuardDepth(depth);
        if (payload.Length == 0 || payload[0] != _nativeStringMarker)
            return Encoding.UTF8.GetString(payload);

        if (payload.Length < 3)
            throw new InvalidDataException("A compact native string is truncated.");

        if (payload[1] is _lowerHexString or _upperHexString)
        {
            string hex = Convert.ToHexString(payload.AsSpan(2));
            return payload[1] == _lowerHexString ? hex.ToLowerInvariant() : hex;
        }

        if (payload[1] != _sourceReferenceString)
            throw new InvalidDataException("Unsupported compact native string format.");

        using MemoryStream input = new(payload, false);
        input.Position = 2;
        string prefix = ReadString(checked((long)ReadUnsigned(input)), depth + 1);
        string kind = ReadString(checked((long)ReadUnsigned(input)), depth + 1);
        string counter = ReadUnsigned(input).ToString(CultureInfo.InvariantCulture);
        if (input.Position != input.Length)
            throw new InvalidDataException("A compact native reference has trailing input.");

        return prefix + _separator + kind + _separator + counter;
    }
}
