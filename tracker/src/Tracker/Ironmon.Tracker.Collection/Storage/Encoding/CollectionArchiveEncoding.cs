using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Encodes closed scope metadata as exact typed positional rows instead of tagged JSON values.
/// </summary>
internal static class CollectionArchiveEncoding
{
    private const string _format = "format";
    private const string _scope = "scope";
    private const string _families = "families";
    private const string _family = "family";
    private const string _series = "series";
    private const string _tuple = "tuple";
    private const string _cells = "cells";
    private const string _sets = "sets";
    private const string _coverage = "coverage";
    private const string _ordered = "ordered";
    private const string _nullTag = "n";
    private const string _integerTag = "i";
    private const string _textTag = "t";
    private const string _blobTag = "b";
    private const int _maximumBytes = 64 * 1024 * 1024;
    private static readonly byte[] _magic = [65, 67, 51];

    /// <summary>
    /// Encodes every original frame field and SQLite storage class without dictionary rows for revisions.
    /// </summary>
    /// <param name="value">The complete version-2 logical scope frame.</param>
    /// <returns>The lossless version-3 archive body.</returns>
    public static byte[] Encode(JsonElement value)
    {
        if (value.EnumerateObject().Count() != 3 || value.GetProperty(_format).GetInt32() != 2)
            throw new InvalidDataException("Unexpected logical archive header.");

        using MemoryStream output = new();
        output.Write(_magic);
        long scope = value.GetProperty(_scope).GetInt64();
        WriteUnsigned(output, checked((ulong)scope));
        JsonElement families = value.GetProperty(_families);
        WriteUnsigned(output, (ulong)families.GetArrayLength());
        foreach (JsonElement entry in families.EnumerateArray())
        {
            long family = entry[0].GetInt64();
            JsonElement frame = entry[1];
            if (entry.GetArrayLength() != 2 || frame.EnumerateObject().Count() != 5 || frame.GetProperty(_format).GetInt32() != 2 || frame.GetProperty(_family).GetInt64() != family || frame.GetProperty(_scope).GetInt64() != scope)
                throw new InvalidDataException("Unexpected logical archive family.");

            WriteUnsigned(output, checked((ulong)family));
            JsonElement series = frame.GetProperty(_series);
            WriteUnsigned(output, (ulong)series.GetArrayLength());
            foreach (JsonElement item in series.EnumerateArray())
            {
                if (item.EnumerateObject().Count() != 4)
                    throw new InvalidDataException("Unexpected logical archive series.");

                WriteUnsigned(output, checked((ulong)item.GetProperty(_tuple).GetInt64()));
                WriteRows(output, item.GetProperty(_cells), 7);
                WriteRows(output, item.GetProperty(_sets), 2);
                WriteRows(output, item.GetProperty(_coverage), 6);
            }

            WriteRows(output, frame.GetProperty(_ordered), 8);
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(output.Length, _maximumBytes);
        return output.ToArray();
    }

    /// <summary>
    /// Rebuilds the exact logical scope frame expected by continuation and historical readers.
    /// </summary>
    /// <param name="raw">The checked version-3 archive body.</param>
    /// <returns>An independent complete version-2 logical JSON frame.</returns>
    public static JsonElement Decode(byte[] raw)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(raw.Length, _maximumBytes);
        using MemoryStream input = new(raw, false);
        foreach (byte expected in _magic)
        {
            if (input.ReadByte() != expected)
                throw new InvalidDataException("Invalid typed archive header.");
        }

        using MemoryStream output = new();
        using (Utf8JsonWriter writer = new(output))
        {
            ulong scope = ReadUnsigned(input);
            writer.WriteStartObject();
            writer.WriteNumber(_format, 2);
            writer.WriteNumber(_scope, scope);
            writer.WriteStartArray(_families);
            int families = ReadCount(input);
            for (int index = 0; index < families; index++)
            {
                ulong family = ReadUnsigned(input);
                writer.WriteStartArray();
                writer.WriteNumberValue(family);
                writer.WriteStartObject();
                writer.WriteNumber(_format, 2);
                writer.WriteNumber(_family, family);
                writer.WriteNumber(_scope, scope);
                writer.WriteStartArray(_series);
                int series = ReadCount(input);
                for (int ordinal = 0; ordinal < series; ordinal++)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber(_tuple, ReadUnsigned(input));
                    ReadRows(input, writer, _cells, 7);
                    ReadRows(input, writer, _sets, 2);
                    ReadRows(input, writer, _coverage, 6);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                ReadRows(input, writer, _ordered, 8);
                writer.WriteEndObject();
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        if (input.Position != input.Length)
            throw new InvalidDataException("A typed archive has trailing input.");

        using JsonDocument document = JsonDocument.Parse(output.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Writes exact tagged SQL values using the fixed row shape declared by this format.
    /// </summary>
    /// <param name="output">The enclosing archive body.</param>
    /// <param name="rows">The complete original tagged rows.</param>
    /// <param name="width">The required number of columns.</param>
    private static void WriteRows(Stream output, JsonElement rows, int width)
    {
        WriteUnsigned(output, (ulong)rows.GetArrayLength());
        foreach (JsonElement row in rows.EnumerateArray())
        {
            if (row.GetArrayLength() != width)
                throw new InvalidDataException("A typed archive row has an unexpected width.");

            foreach (JsonElement cell in row.EnumerateArray())
            {
                string? tag = cell[0].GetString();
                if (tag == _nullTag && cell.GetArrayLength() == 1)
                {
                    output.WriteByte(0);
                }
                else if (tag == _integerTag && cell.GetArrayLength() == 2)
                {
                    long number = cell[1].GetInt64();
                    output.WriteByte(1);
                    WriteUnsigned(output, unchecked((ulong)((number << 1) ^ (number >> 63))));
                }
                else if (tag == _textTag && cell.GetArrayLength() == 2)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(cell[1].GetString()!);
                    output.WriteByte(2);
                    WriteUnsigned(output, (ulong)bytes.Length);
                    output.Write(bytes);
                }
                else if (tag == _blobTag && cell.GetArrayLength() == 2)
                {
                    string hex = cell[1].GetString()!;
                    byte[] bytes = Convert.FromHexString(hex);
                    if (Convert.ToHexString(bytes) != hex)
                        throw new InvalidDataException("A typed SQL BLOB has noncanonical hexadecimal spelling.");

                    if (bytes.Length == 8 && BinaryPrimitives.ReadUInt64BigEndian(bytes) < (1UL << 56))
                    {
                        output.WriteByte(4);
                        WriteUnsigned(output, BinaryPrimitives.ReadUInt64BigEndian(bytes));
                    }
                    else
                    {
                        output.WriteByte(3);
                        WriteUnsigned(output, (ulong)bytes.Length);
                        output.Write(bytes);
                    }
                }
                else
                {
                    throw new InvalidDataException("A typed archive has an unknown SQL storage class.");
                }
            }
        }
    }

    /// <summary>
    /// Restores original SQL class tags without changing integer precision or BLOB bytes.
    /// </summary>
    /// <param name="input">The archive stream positioned at a row count.</param>
    /// <param name="writer">The logical JSON destination.</param>
    /// <param name="name">The original row category name.</param>
    /// <param name="width">The versioned number of columns.</param>
    private static void ReadRows(Stream input, Utf8JsonWriter writer, string name, int width)
    {
        writer.WriteStartArray(name);
        int rows = ReadCount(input);
        for (int row = 0; row < rows; row++)
        {
            writer.WriteStartArray();
            for (int column = 0; column < width; column++)
            {
                int kind = input.ReadByte();
                writer.WriteStartArray();
                if (kind == 0)
                {
                    writer.WriteStringValue(_nullTag);
                }
                else if (kind == 1)
                {
                    ulong encoded = ReadUnsigned(input);
                    long number = (long)(encoded >> 1);
                    writer.WriteStringValue(_integerTag);
                    writer.WriteNumberValue((encoded & 1) == 0 ? number : ~number);
                }
                else if (kind is 2 or 3)
                {
                    byte[] bytes = new byte[ReadCount(input)];
                    input.ReadExactly(bytes);
                    writer.WriteStringValue(kind == 2 ? _textTag : _blobTag);
                    writer.WriteStringValue(kind == 2 ? Encoding.UTF8.GetString(bytes) : Convert.ToHexString(bytes));
                }
                else if (kind == 4)
                {
                    byte[] bytes = new byte[8];
                    BinaryPrimitives.WriteUInt64BigEndian(bytes, ReadUnsigned(input));
                    writer.WriteStringValue(_blobTag);
                    writer.WriteStringValue(Convert.ToHexString(bytes));
                }
                else
                {
                    throw new InvalidDataException("A typed archive has an invalid SQL type tag.");
                }

                writer.WriteEndArray();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes one unsigned native integer without allocating an array per byte.
    /// </summary>
    /// <param name="output">Receives the canonical base-128 value.</param>
    /// <param name="value">The complete unsigned value.</param>
    private static void WriteUnsigned(Stream output, ulong value)
    {
        do
        {
            byte part = (byte)(value & 127);
            value >>= 7;
            output.WriteByte(value == 0 ? part : (byte)(part | 128));
        } while (value != 0);
    }

    /// <summary>
    /// Reads exactly one bounded native unsigned varint and rejects overflow or redundant bytes.
    /// </summary>
    /// <param name="input">The enclosing archive stream.</param>
    /// <returns>The exact unsigned value.</returns>
    private static ulong ReadUnsigned(Stream input)
    {
        ulong value = 0;
        for (int shift = 0; shift < 70; shift += 7)
        {
            int part = input.ReadByte();
            if (part < 0 || shift == 63 && part > 1)
                throw new InvalidDataException("An archive integer is truncated or overflows.");

            value |= (ulong)(part & 127) << shift;
            if ((part & 128) == 0)
            {
                if (shift != 0 && part == 0)
                    throw new InvalidDataException("An archive integer is not canonically encoded.");

                return value;
            }
        }

        throw new InvalidDataException("An archive integer exceeds its supported width.");
    }

    /// <summary>
    /// Bounds declared lengths by remaining input before allocating or iterating.
    /// </summary>
    /// <param name="input">The enclosing archive stream.</param>
    /// <returns>A count no larger than the remaining byte count.</returns>
    private static int ReadCount(Stream input)
    {
        int count = checked((int)ReadUnsigned(input));
        if (count > input.Length - input.Position)
            throw new InvalidDataException("An archive count exceeds its body.");

        return count;
    }
}
