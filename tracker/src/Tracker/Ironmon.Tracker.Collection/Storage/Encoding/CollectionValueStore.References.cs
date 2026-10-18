using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Traces retained SQL and binary references before reclaiming unreachable dictionary rows.
/// </summary>
internal sealed partial class CollectionValueStore
{

    /// <summary>
    /// Adds only string and property-name references from a complete version-2 binary detail body.
    /// </summary>
    /// <param name="body">The complete uncompressed binary body.</param>
    /// <param name="roots">Receives the directly referenced immutable value IDs.</param>
    public static void CollectReferences(byte[] body, ISet<long> roots)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(body.Length, _maximumRecordBytes);
        using MemoryStream input = new(body, false);
        CollectInline(input, roots, 0);
        if (input.Position != input.Length)
            throw new InvalidDataException("A referenced binary body has trailing input.");
    }

    /// <summary>
    /// Removes values that cannot be reached from any retained SQL field or encoded detail.
    /// </summary>
    /// <param name="roots">The complete set of retained direct value references.</param>
    public void ReclaimUnreferenced(ISet<long> roots)
    {
        Dictionary<long, HashSet<long>> children = [];
        foreach (CollectionValueReferenceRecord stored in CollectionQueries.FindValueReferences(connection, transaction()))
        {
            long id = stored.Id;
            ValueKind kind = (ValueKind)stored.Kind;
            byte[] payload = stored.Payload;
            if (!SHA256.HashData(payload).AsSpan(0, 8).SequenceEqual(stored.Digest))
                throw new InvalidDataException("A referenced dictionary value failed its integrity check.");

            HashSet<long> references = [];
            using MemoryStream input = new(payload, false);
            if (kind is ValueKind.Array or ValueKind.Object)
            {
                while (input.Position < input.Length)
                    references.Add(checked((long)ReadUnsigned(input)));
            }
            else if (kind == ValueKind.String && payload.Length > 1 && payload[0] == _nativeStringMarker && payload[1] == _sourceReferenceString)
            {
                input.Position = 2;
                references.Add(checked((long)ReadUnsigned(input)));
                references.Add(checked((long)ReadUnsigned(input)));
                ReadUnsigned(input);
                if (input.Position != input.Length)
                    throw new InvalidDataException("A compact source reference has trailing input.");
            }

            children[id] = references;
        }

        HashSet<long> reached = [];
        Stack<long> pending = new(roots);
        while (pending.TryPop(out long id))
        {
            if (!reached.Add(id))
                continue;

            if (!children.TryGetValue(id, out HashSet<long>? references))
                throw new InvalidDataException("A retained collection value reference is missing.");

            foreach (long child in references)
                pending.Push(child);
        }

        using SqliteCommand delete = CollectionQueries.CreateDeleteUnreferencedValueCommand(connection, transaction(), 0L);
        foreach (long id in children.Keys.Where(id => !reached.Contains(id)))
        {
            delete.Parameters[0].Value = id;
            delete.ExecuteNonQuery();
        }

        ClearCaches();
    }

    /// <summary>
    /// Traverses one exact inline value without materializing its JSON representation.
    /// </summary>
    /// <param name="input">The bounded source stream.</param>
    /// <param name="roots">Receives directly referenced string IDs.</param>
    /// <param name="depth">The current nesting depth.</param>
    private static void CollectInline(Stream input, ISet<long> roots, int depth)
    {
        GuardDepth(depth);
        ValueKind kind = (ValueKind)input.ReadByte();
        if (kind is ValueKind.Array or ValueKind.Object)
        {
            int count = checked((int)ReadUnsigned(input));
            if (count > input.Length - input.Position)
                throw new InvalidDataException("A reference collection count exceeds its body.");

            for (int index = 0; index < count; index++)
            {
                if (kind == ValueKind.Object)
                    roots.Add(checked((long)ReadUnsigned(input)));

                CollectInline(input, roots, depth + 1);
            }
        }
        else if (kind == ValueKind.String)
        {
            roots.Add(checked((long)ReadUnsigned(input)));
        }
        else if (kind == ValueKind.Integer)
        {
            ReadUnsigned(input);
        }
        else if (kind == ValueKind.NumberToken)
        {
            int count = checked((int)ReadUnsigned(input));
            if (count > input.Length - input.Position)
                throw new InvalidDataException("A referenced numeric token is truncated.");

            input.Position += count;
        }
        else if (kind is not (ValueKind.Null or ValueKind.False or ValueKind.True))
        {
            throw new InvalidDataException("An encoded reference has an unknown type tag.");
        }
    }
}
