using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Stores exact typed values and encodes detail records using shared numeric string references.
/// </summary>
/// <remarks>
/// Creates a value store on the owning writer's connection and current transaction.
/// Complex lookup values share child records; archive bodies inline numeric values instead.
/// </remarks>
/// <param name="connection">The serialized collection connection owning the immutable records.</param>
/// <param name="transaction">Returns the writer's current transaction, or null outside a transaction.</param>
/// <param name="compactNativeStrings">Whether new native strings use the version-3 compact representation.</param>
internal sealed partial class CollectionValueStore(SqliteConnection connection, Func<SqliteTransaction?> transaction, bool compactNativeStrings = true)
{
    private const string _separator = ":";
    private const int _maximumCacheEntries = 32768;
    private const int _maximumDepth = 128;
    private const int _maximumRecordBytes = 64 * 1024 * 1024;
    private readonly Dictionary<string, long> _interned = new(StringComparer.Ordinal);
    private readonly Dictionary<long, (ValueKind Kind, byte[] Payload)> _read = [];
    private bool _compactNativeStrings = compactNativeStrings;

    /// <summary>
    /// Identifies the exact primitive or structured representation without repeating textual type names.
    /// </summary>
    private enum ValueKind : byte
    {
        Null = 0,
        False = 1,
        True = 2,
        Integer = 3,
        NumberToken = 4,
        String = 5,
        Array = 6,
        Object = 7
    }

    /// <summary>
    /// Clears transaction-dependent identities after rollback so rolled-back row IDs cannot be reused.
    /// </summary>
    public void ClearCaches()
    {
        _interned.Clear();
        _read.Clear();
    }

    /// <summary>
    /// Interns a lossless lookup value whose structured children are shared by numeric reference.
    /// </summary>
    /// <param name="value">The exact value, including numeric-token and string-versus-number distinctions.</param>
    /// <returns>The immutable database value ID.</returns>
    public long Intern(JsonElement value)
    {
        ValueKind kind = Kind(value);
        using MemoryStream output = new();
        if (kind == ValueKind.Array)
        {
            foreach (JsonElement child in value.EnumerateArray())
                WriteUnsigned(output, Intern(child));
        }
        else if (kind == ValueKind.Object)
        {
            foreach (JsonProperty child in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                WriteUnsigned(output, Intern(JsonSerializer.SerializeToElement(child.Name)));
                WriteUnsigned(output, Intern(child.Value));
            }
        }
        else
        {
            WritePrimitive(output, kind, value);
        }

        byte[] payload = output.ToArray();
        string key = ((byte)kind).ToString(CultureInfo.InvariantCulture) + _separator + Convert.ToHexString(payload);
        if (_interned.TryGetValue(key, out long cached))
            return cached;

        byte[] digest = SHA256.HashData(payload)[..8];
        foreach (CollectionValueCandidate candidate in CollectionQueries.FindValues(connection, transaction(), (int)kind, digest))
        {
            if (candidate.Payload.AsSpan().SequenceEqual(payload))
                return Cache(key, candidate.Id);
        }

        return Cache(key, CollectionQueries.InsertValue(connection, transaction(), (int)kind, payload, digest));
    }

    /// <summary>
    /// Encodes a detail body with typed numbers and immutable numeric references for strings and field names.
    /// </summary>
    /// <param name="value">The complete detail record whose facts must be retained exactly.</param>
    /// <returns>A version-2 binary value body; its enclosing schema or block supplies the format version.</returns>
    public byte[] Encode(JsonElement value)
    {
        using MemoryStream output = new();
        WriteInline(output, value, 0);
        return output.ToArray();
    }

    /// <summary>
    /// Decodes one binary detail body without losing integer precision or number-token spelling.
    /// </summary>
    /// <param name="body">The complete bounded version-2 binary body.</param>
    /// <returns>An independent exact JSON value for the reducer or diagnostic reader.</returns>
    public JsonElement Decode(byte[] body)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(body.Length, _maximumRecordBytes);
        using MemoryStream input = new(body, false);
        using MemoryStream output = new();
        using (Utf8JsonWriter writer = new(output))
            ReadInline(input, writer, 0);

        if (input.Position != input.Length)
            throw new InvalidDataException("A binary collection value has trailing input.");

        using JsonDocument document = JsonDocument.Parse(output.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Resolves an immutable lookup value and all of its referenced children.
    /// </summary>
    /// <param name="id">The immutable value ID from a real database foreign key.</param>
    /// <returns>An independent exact value.</returns>
    public JsonElement Read(long id)
    {
        using MemoryStream output = new();
        using (Utf8JsonWriter writer = new(output))
            ReadReference(id, writer, 0);

        using JsonDocument document = JsonDocument.Parse(output.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Caches a committed-or-current-transaction intern result within the configured memory bound.
    /// </summary>
    /// <param name="key">The complete exact typed payload cache key.</param>
    /// <param name="id">The immutable value ID represented by the key.</param>
    /// <returns>The supplied ID.</returns>
    private long Cache(string key, long id)
    {
        if (_interned.Count < _maximumCacheEntries)
            _interned[key] = id;

        return id;
    }

    /// <summary>
    /// Chooses a type tag while preserving noncanonical or fractional numeric tokens verbatim.
    /// </summary>
    /// <param name="value">The source value to classify.</param>
    /// <returns>The explicit binary type.</returns>
    private static ValueKind Kind(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => ValueKind.Null,
            JsonValueKind.False => ValueKind.False,
            JsonValueKind.True => ValueKind.True,
            JsonValueKind.String => ValueKind.String,
            JsonValueKind.Array => ValueKind.Array,
            JsonValueKind.Object => ValueKind.Object,
            JsonValueKind.Number => BigInteger.TryParse(value.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger integer) && integer.ToString(CultureInfo.InvariantCulture) == value.GetRawText() ? ValueKind.Integer : ValueKind.NumberToken,
            _ => throw new InvalidDataException("Unsupported collection value type.")
        };

    /// <summary>
    /// Writes a primitive value's payload after its caller has supplied the type tag.
    /// </summary>
    /// <param name="output">Receives the exact primitive bytes.</param>
    /// <param name="kind">The explicit primitive type.</param>
    /// <param name="value">The primitive source value.</param>
    private void WritePrimitive(Stream output, ValueKind kind, JsonElement value)
    {
        if (kind == ValueKind.Integer)
        {
            BigInteger number = BigInteger.Parse(value.GetRawText(), CultureInfo.InvariantCulture);
            output.Write(CollectionEncoding.Unsigned(number.Sign < 0 ? -number * 2 - 1 : number * 2));
        }
        else if (kind == ValueKind.String)
        {
            string text = value.GetString()!;
            output.Write(_compactNativeStrings ? EncodeNativeString(text, component => Intern(JsonSerializer.SerializeToElement(component))) : Encoding.UTF8.GetBytes(text));
        }
        else if (kind == ValueKind.NumberToken)
        {
            output.Write(Encoding.UTF8.GetBytes(value.GetRawText()));
        }
    }

    /// <summary>
    /// Writes an inline typed record, sharing only string identities rather than creating one row per numeric cell.
    /// </summary>
    /// <param name="output">Receives the binary body.</param>
    /// <param name="value">The current source value.</param>
    /// <param name="depth">The current nesting depth.</param>
    private void WriteInline(Stream output, JsonElement value, int depth)
    {
        GuardDepth(depth);
        ValueKind kind = Kind(value);
        output.WriteByte((byte)kind);
        if (kind == ValueKind.Array)
        {
            WriteUnsigned(output, value.GetArrayLength());
            foreach (JsonElement child in value.EnumerateArray())
                WriteInline(output, child, depth + 1);
        }
        else if (kind == ValueKind.Object)
        {
            JsonProperty[] properties = [.. value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal)];
            WriteUnsigned(output, properties.Length);
            foreach (JsonProperty property in properties)
            {
                WriteUnsigned(output, Intern(JsonSerializer.SerializeToElement(property.Name)));
                WriteInline(output, property.Value, depth + 1);
            }
        }
        else if (kind == ValueKind.String)
        {
            WriteUnsigned(output, Intern(value));
        }
        else if (kind == ValueKind.NumberToken)
        {
            byte[] token = Encoding.UTF8.GetBytes(value.GetRawText());
            WriteUnsigned(output, token.Length);
            output.Write(token);
        }
        else
        {
            WritePrimitive(output, kind, value);
        }
    }

    /// <summary>
    /// Reads a shared immutable record after validating its stored digest.
    /// </summary>
    /// <param name="id">The referenced record ID.</param>
    /// <returns>The explicit type and its exact payload.</returns>
    private (ValueKind Kind, byte[] Payload) Record(long id)
    {
        if (_read.TryGetValue(id, out var cached))
            return cached;

        CollectionValueRecord value = CollectionQueries.ReadValue(connection, transaction(), id) ?? throw new InvalidDataException("An immutable collection value reference is missing.");
        var record = ((ValueKind)value.Kind, value.Payload);
        if (!SHA256.HashData(record.Item2).AsSpan(0, 8).SequenceEqual(value.Digest))
            throw new InvalidDataException("An immutable collection value failed its integrity check.");

        if (_read.Count < _maximumCacheEntries)
            _read[id] = record;

        return record;
    }

    /// <summary>
    /// Writes a referenced value to the exact reducer-facing JSON representation.
    /// </summary>
    /// <param name="id">The referenced immutable value ID.</param>
    /// <param name="writer">Receives the decoded JSON tokens.</param>
    /// <param name="depth">The current nesting depth, bounding damaged cycles.</param>
    private void ReadReference(long id, Utf8JsonWriter writer, int depth)
    {
        GuardDepth(depth);
        var (kind, payload) = Record(id);
        using MemoryStream input = new(payload, false);
        if (kind == ValueKind.Array)
        {
            writer.WriteStartArray();
            while (input.Position < input.Length)
                ReadReference(checked((long)ReadUnsigned(input)), writer, depth + 1);

            writer.WriteEndArray();
        }
        else if (kind == ValueKind.Object)
        {
            writer.WriteStartObject();
            while (input.Position < input.Length)
            {
                writer.WritePropertyName(ReadString(checked((long)ReadUnsigned(input))));
                ReadReference(checked((long)ReadUnsigned(input)), writer, depth + 1);
            }

            writer.WriteEndObject();
        }
        else if (kind == ValueKind.String)
        {
            writer.WriteStringValue(DecodeNativeString(payload, 0));
        }
        else if (kind == ValueKind.NumberToken)
        {
            writer.WriteRawValue(payload);
        }
        else
        {
            ReadPrimitive(input, writer, kind);
        }
    }

    /// <summary>
    /// Reads a field-name or string-value reference and rejects references to other value types.
    /// </summary>
    /// <param name="id">The immutable string ID.</param>
    /// <param name="depth">The current dependency depth, including compact source components.</param>
    /// <returns>The exact UTF-8 string.</returns>
    private string ReadString(long id, int depth = 0)
    {
        GuardDepth(depth);
        var (kind, payload) = Record(id);
        return kind == ValueKind.String ? DecodeNativeString(payload, depth + 1) : throw new InvalidDataException("A collection string reference has the wrong type.");
    }

    /// <summary>
    /// Reads one inline binary record and validates its bounded collection lengths.
    /// </summary>
    /// <param name="input">The binary record stream.</param>
    /// <param name="writer">Receives the decoded JSON tokens.</param>
    /// <param name="depth">The current nesting depth.</param>
    private void ReadInline(Stream input, Utf8JsonWriter writer, int depth)
    {
        GuardDepth(depth);
        int tag = input.ReadByte();
        if (tag < 0)
            throw new EndOfStreamException();

        ValueKind kind = (ValueKind)tag;
        if (kind is ValueKind.Array or ValueKind.Object)
        {
            int count = checked((int)ReadUnsigned(input));
            if (count > input.Length - input.Position)
                throw new InvalidDataException("A collection record declares an invalid element count.");

            if (kind == ValueKind.Array)
            {
                writer.WriteStartArray();
            }
            else
            {
                writer.WriteStartObject();
            }

            for (int index = 0; index < count; index++)
            {
                if (kind == ValueKind.Object)
                    writer.WritePropertyName(ReadString(checked((long)ReadUnsigned(input))));

                ReadInline(input, writer, depth + 1);
            }

            if (kind == ValueKind.Array)
            {
                writer.WriteEndArray();
            }
            else
            {
                writer.WriteEndObject();
            }
        }
        else if (kind == ValueKind.String)
        {
            writer.WriteStringValue(ReadString(checked((long)ReadUnsigned(input))));
        }
        else if (kind == ValueKind.NumberToken)
        {
            int length = checked((int)ReadUnsigned(input));
            if (length > input.Length - input.Position)
                throw new InvalidDataException("A collection number token exceeds its record.");

            byte[] token = new byte[length];
            input.ReadExactly(token);
            writer.WriteRawValue(token);
        }
        else
        {
            ReadPrimitive(input, writer, kind);
        }
    }

    /// <summary>
    /// Reads one fixed-tag primitive, including arbitrary precision signed integers.
    /// </summary>
    /// <param name="input">The primitive payload stream.</param>
    /// <param name="writer">Receives the exact value.</param>
    /// <param name="kind">The explicit primitive tag.</param>
    private static void ReadPrimitive(Stream input, Utf8JsonWriter writer, ValueKind kind)
    {
        if (kind == ValueKind.Integer)
        {
            BigInteger encoded = ReadUnsigned(input);
            BigInteger integer = encoded.IsEven ? encoded / 2 : -(encoded + 1) / 2;
            writer.WriteRawValue(integer.ToString(CultureInfo.InvariantCulture));
        }
        else if (kind == ValueKind.Null)
        {
            writer.WriteNullValue();
        }
        else if (kind is ValueKind.False or ValueKind.True)
        {
            writer.WriteBooleanValue(kind == ValueKind.True);
        }
        else
        {
            throw new InvalidDataException("Unknown collection binary type tag.");
        }
    }

    /// <summary>
    /// Writes one nonnegative count or immutable ID as a canonical unsigned varint.
    /// </summary>
    /// <param name="output">Receives the compact integer.</param>
    /// <param name="value">The nonnegative ID or count.</param>
    private static void WriteUnsigned(Stream output, long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ulong remaining = (ulong)value;
        do
        {
            byte part = (byte)(remaining & 127);
            remaining >>= 7;
            output.WriteByte(remaining == 0 ? part : (byte)(part | 128));
        } while (remaining != 0);
    }

    /// <summary>
    /// Reads exactly one canonical unsigned integer from the enclosing record.
    /// </summary>
    /// <param name="input">The stream positioned at the integer.</param>
    /// <returns>The exact nonnegative integer.</returns>
    private static BigInteger ReadUnsigned(Stream input)
    {
        BigInteger result = BigInteger.Zero;
        int shift = 0;
        while (true)
        {
            int part = input.ReadByte();
            if (part < 0 || shift > _maximumRecordBytes * 7)
                throw new InvalidDataException("A collection integer is truncated or exceeds its limit.");

            result |= (BigInteger)(part & 127) << shift;
            if ((part & 128) == 0)
            {
                if (shift != 0 && part == 0)
                    throw new InvalidDataException("A collection integer is not canonically encoded.");

                return result;
            }

            shift = checked(shift + 7);
        }
    }

    /// <summary>
    /// Rejects damaged or unsupported excessively nested value graphs.
    /// </summary>
    /// <param name="depth">The current nesting depth.</param>
    private static void GuardDepth(int depth)
    {
        if (depth > _maximumDepth)
            throw new InvalidDataException("A collection value exceeds the supported nesting depth.");
    }
}
