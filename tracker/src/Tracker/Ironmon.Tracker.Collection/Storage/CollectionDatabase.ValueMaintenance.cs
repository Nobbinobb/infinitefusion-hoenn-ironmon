using Ironmon.Tracker.Collection.Queries.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Storage.Encoding;
using System.Text.Json;

namespace Ironmon.Tracker.Collection.Storage;

/// <summary>
/// Retains dictionary values reachable from both relational facts and nested binary archive states.
/// </summary>
internal sealed partial class CollectionDatabase
{

    /// <summary>
    /// Reclaims obsolete dictionary representations only after all retained archive and owned references are traced.
    /// </summary>
    private void ReclaimDictionaryValues()
    {
        HashSet<long> roots = [];
        foreach (var row in HistoryRows(_historyRoots))
            roots.Add((long)Unpack(row["value_id"]));

        foreach (ValueRootRecord row in CollectionQueries.FindRelationalValueRoots(_db, _transaction))
            roots.Add(row.Value);

        foreach (BinaryRootRecord row in CollectionQueries.FindBinaryValueRoots(_db, _transaction))
            CollectionValueStore.CollectReferences(row.Value!, roots);

        foreach (OrderedStateRecord state in CollectionQueries.FindOrderedReferenceStates(_db, _transaction))
            CollectionValueStore.CollectReferences(CollectionEncoding.Inflate(state.Payload, _maximumBatchBytes), roots);

        foreach (ReferenceBlockRecord block in CollectionQueries.FindReferenceBlocks(_db, _transaction))
        {
            if (block.Format == 2)
            {
                byte[] raw = block.Codec == _rawBlockCodec ? block.Payload : CollectionEncoding.Inflate(block.Payload, _maximumBatchBytes);
                CollectionValueStore.CollectReferences(raw, roots);
            }
        }

        List<long> archives = [.. CollectionQueries.FindArchiveValueRoots(_db, _transaction).Select(row => row.Value)];

        foreach (long block in archives)
        {
            JsonElement value = ReadStoredBlock(block);
            if (value.TryGetProperty(_archiveFamiliesProperty, out JsonElement families))
            {
                foreach (JsonElement entry in families.EnumerateArray())
                    CollectOrderedArchiveRoots(entry[1], roots);
            }
            else
            {
                CollectOrderedArchiveRoots(value, roots);
            }
        }

        _values.ReclaimUnreferenced(roots);
        _archiveReadCache.Clear();
    }

    /// <summary>
    /// Traces value references inside compressed ordered states nested in a closed SQL frame.
    /// </summary>
    /// <param name="frame">The complete family frame.</param>
    /// <param name="roots">Receives the retained immutable string references.</param>
    private static void CollectOrderedArchiveRoots(JsonElement frame, ISet<long> roots)
    {
        foreach (JsonElement row in frame.GetProperty(_archiveOrderedProperty).EnumerateArray())
        {
            if (row[3][1].GetString() == _orderedValueCodec)
                CollectionValueStore.CollectReferences(CollectionEncoding.Inflate(Convert.FromHexString(row[4][1].GetString()!), _maximumBatchBytes), roots);
        }
    }
}
