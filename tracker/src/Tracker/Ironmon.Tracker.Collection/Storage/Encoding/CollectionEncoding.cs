using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ironmon.Tracker.Collection.Storage.Encoding;

/// <summary>
/// Provides canonical, lossless collector encodings independently of gameplay RNG.
/// </summary>
internal static class CollectionEncoding
{
    private const string _compactGuid = "N";

    /// <summary>
    /// Encodes one ordered unsigned revision.
    /// </summary>
    /// <param name="value">The unsigned revision or source sequence to encode in sortable big-endian form.</param>
    /// <returns>The fixed-width eight-byte big-endian encoding used for lexical revision ordering.</returns>
    public static byte[] Revision(ulong value)
    {
        byte[] bytes = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    /// <summary>
    /// Decodes one ordered unsigned revision.
    /// </summary>
    /// <param name="value">The stored eight-byte big-endian revision.</param>
    /// <returns>The unsigned revision read from its big-endian bytes.</returns>
    public static ulong ReadRevision(byte[] value)
        => BinaryPrimitives.ReadUInt64BigEndian(value);

    /// <summary>
    /// Parses an explicitly lossless wire sequence.
    /// </summary>
    /// <param name="value">The JSON string containing the complete unsigned decimal source sequence.</param>
    /// <returns>The exact unsigned source sequence parsed without floating-point conversion.</returns>
    public static ulong Sequence(JsonElement value)
        => ulong.Parse(value.GetString() ?? throw new InvalidDataException("Sequence must be an unsigned decimal string."), CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a UUID without using gameplay random state.
    /// </summary>
    /// <param name="value">The compact 32-hex-character UUID string to parse.</param>
    /// <returns>The UUID in its 16-byte database representation.</returns>
    public static byte[] Identifier(string value)
        => Guid.ParseExact(value, _compactGuid).ToByteArray();

    /// <summary>
    /// Formats a persisted UUID for a wire binding.
    /// </summary>
    /// <param name="value">The persisted 16-byte UUID to format for the wire.</param>
    /// <returns>The UUID as a compact 32-hex-character wire string.</returns>
    public static string Identifier(byte[] value)
        => new Guid(value).ToString(_compactGuid);

    /// <summary>
    /// Derives a stable technical identifier in a separate namespace.
    /// </summary>
    /// <param name="value">The stable technical input string to hash without consuming gameplay randomness.</param>
    /// <returns>The first 16 bytes of the UTF-8 input's SHA-256 digest as a stable technical identifier.</returns>
    public static byte[] DerivedIdentifier(string value)
        => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))[..16];

    /// <summary>
    /// Encodes an unsigned integer using canonical base-128 varints.
    /// </summary>
    /// <param name="value">The exact nonnegative integer to encode.</param>
    /// <returns>The canonical unsigned base-128 variable-length encoding, including one byte for zero.</returns>
    public static byte[] Unsigned(BigInteger value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        List<byte> bytes = [];
        do
        {
            byte part = (byte)(value & 127);
            value >>= 7;
            bytes.Add(value.IsZero ? part : (byte)(part | 128));
        } while (!value.IsZero);

        return [.. bytes];
    }

    /// <summary>
    /// Decodes and validates a canonical unsigned varint.
    /// </summary>
    /// <param name="bytes">The complete canonical unsigned varint to validate and decode.</param>
    /// <returns>The exact nonnegative integer after canonical-encoding validation.</returns>
    public static BigInteger ReadUnsigned(byte[] bytes)
    {
        BigInteger result = BigInteger.Zero;
        int shift = 0;
        foreach (byte part in bytes)
        {
            result |= (BigInteger)(part & 127) << shift;
            shift = checked(shift + 7);
            if ((part & 128) == 0)
                break;
        }

        if (!Unsigned(result).AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("Noncanonical integer encoding.");

        return result;
    }

    /// <summary>
    /// Stores native signed integers compactly and wider values exactly.
    /// </summary>
    /// <param name="value">The exact signed scalar, or null to store an explicit SQLite database null.</param>
    /// <returns>A native signed 64-bit integer, a canonical signed-varint blob, or DBNull.Value for null.</returns>
    public static object Scalar(BigInteger? value)
    {
        if (value is null)
            return DBNull.Value;

        BigInteger integer = value.Value;
        if (integer >= long.MinValue && integer <= long.MaxValue)
            return (long)integer;

        return Unsigned(integer.Sign < 0 ? -integer * 2 - 1 : integer * 2);
    }

    /// <summary>
    /// Reads native signed integers or canonical wide signed varints.
    /// </summary>
    /// <param name="value">The native signed integer, canonical signed-varint blob or database-null value.</param>
    /// <returns>The exact signed scalar, or null for SQLite database null.</returns>
    public static BigInteger? ReadScalar(object value)
    {
        if (value is DBNull)
            return null;

        if (value is long number)
            return number;

        BigInteger encoded = ReadUnsigned((byte[])value);
        BigInteger result = encoded.IsEven ? encoded / 2 : -(encoded + 1) / 2;
        if (result >= long.MinValue && result <= long.MaxValue)
            throw new InvalidDataException("Wide scalar could have used native integer storage.");

        return result;
    }

    /// <summary>
    /// Stores a nonnegative exact coverage count.
    /// </summary>
    /// <param name="value">The exact nonnegative availability count to store.</param>
    /// <returns>A native signed integer when it fits, or an exact unsigned varint blob for larger counts.</returns>
    public static object Count(BigInteger value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return value <= long.MaxValue ? (long)value : Unsigned(value);
    }

    /// <summary>
    /// Reads an exact coverage count.
    /// </summary>
    /// <param name="value">The native integer or canonical unsigned-varint blob containing a count.</param>
    /// <returns>The exact nonnegative coverage count from native integer or varint storage.</returns>
    public static BigInteger ReadCount(object value)
        => value is long number ? number : ReadUnsigned((byte[])value);

    /// <summary>
    /// Formats an exact value without locale or floating-point conversion.
    /// </summary>
    /// <param name="value">The exact integer or unsigned sequence to format using invariant decimal digits.</param>
    /// <returns>The complete invariant-culture decimal representation without precision loss.</returns>
    public static string Decimal(BigInteger value)
        => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats an unsigned sequence without locale conversion.
    /// </summary>
    /// <param name="value">The exact integer or unsigned sequence to format using invariant decimal digits.</param>
    /// <returns>The complete invariant-culture decimal representation without precision loss.</returns>
    public static string Decimal(ulong value)
        => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Returns deterministic UTF-8 JSON with sorted object keys.
    /// </summary>
    /// <param name="element">The JSON value whose object keys are sorted without changing numeric tokens.</param>
    /// <returns>Deterministic UTF-8 JSON bytes with sorted object keys and unchanged numeric token text.</returns>
    public static byte[] Canonical(JsonElement element)
    {
        using MemoryStream output = new();
        using (Utf8JsonWriter writer = new(output))
            WriteCanonical(writer, element);

        return output.ToArray();
    }

    /// <summary>
    /// Serializes a value using deterministic JSON.
    /// </summary>
    /// <typeparam name="T">The CLR type of the value serialized into canonical JSON.</typeparam>
    /// <param name="value">The value to serialize before recursively sorting JSON object keys.</param>
    /// <returns>Deterministic UTF-8 JSON bytes with sorted object keys and unchanged numeric token text.</returns>
    public static byte[] Canonical<T>(T value)
        => Canonical(JsonSerializer.SerializeToElement(value));

    /// <summary>
    /// Writes canonical JSON without changing exact numeric token text.
    /// </summary>
    /// <param name="writer">The UTF-8 writer receiving canonical JSON.</param>
    /// <param name="element">The JSON value whose object keys are sorted without changing numeric tokens.</param>
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }

            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (JsonElement item in element.EnumerateArray())
                WriteCanonical(writer, item);

            writer.WriteEndArray();
        }
        else if (element.ValueKind == JsonValueKind.Undefined)
        {
            writer.WriteNullValue();
        }
        else
        {
            element.WriteTo(writer);
        }
    }

    /// <summary>
    /// Compresses exact finalized state using the interoperable zlib codec.
    /// </summary>
    /// <param name="bytes">The exact uncompressed bytes to encode as a zlib block.</param>
    /// <returns>A zlib-compressed byte array that reproduces the original bytes exactly.</returns>
    public static byte[] Compress(byte[] bytes)
    {
        using MemoryStream output = new();
        using (ZLibStream compressor = new(output, CompressionLevel.Optimal, true))
            compressor.Write(bytes);

        return output.ToArray();
    }

    /// <summary>
    /// Inflates a bounded block and validates its declared length and digest.
    /// </summary>
    /// <param name="bytes">The stored zlib-compressed immutable block.</param>
    /// <param name="length">The exact expected uncompressed length in bytes.</param>
    /// <param name="digest">The expected SHA-256 digest of the complete uncompressed content.</param>
    /// <returns>The uncompressed bytes after the exact length and SHA-256 digest have both been verified.</returns>
    public static byte[] Decompress(byte[] bytes, long length, byte[] digest)
    {
        using MemoryStream input = new(bytes, false);
        using ZLibStream decompressor = new(input, CompressionMode.Decompress);
        using MemoryStream output = new();
        byte[] buffer = new byte[65536];
        int read;
        while ((read = decompressor.Read(buffer)) > 0)
        {
            if (output.Length + read > length)
                throw new InvalidDataException("Compressed collection block exceeds its declared length.");

            output.Write(buffer, 0, read);
        }

        byte[] result = output.ToArray();
        if (result.LongLength != length || !SHA256.HashData(result).AsSpan().SequenceEqual(digest))
            throw new InvalidDataException("Collection block integrity check failed.");

        return result;
    }

    /// <summary>
    /// Inflates bounded mutable working state whose zlib checksum supplies frame integrity.
    /// </summary>
    /// <param name="bytes">The zlib-compressed mutable working-state checkpoint.</param>
    /// <param name="maximumLength">The inclusive maximum permitted uncompressed length in bytes.</param>
    /// <returns>The uncompressed working-state bytes after enforcing the supplied size limit.</returns>
    public static byte[] Inflate(byte[] bytes, long maximumLength)
    {
        using MemoryStream input = new(bytes, false);
        using ZLibStream decompressor = new(input, CompressionMode.Decompress);
        using MemoryStream output = new();
        byte[] buffer = new byte[65536];
        int read;
        while ((read = decompressor.Read(buffer)) > 0)
        {
            if (output.Length + read > maximumLength)
                throw new InvalidDataException("Collection working state exceeds its declared limit.");

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }
}
