using System.Text.Json.Nodes;

namespace Ironmon.Tracker.Collection.Reduction;

/// <summary>
/// Preserves nested recovery contributions while keeping direct and inclusive source totals distinct.
/// </summary>
public sealed partial class CollectionEventReducer
{
    /// <summary>
    /// Propagates one completed child group's sufficient recipient state into its actual parent once.
    /// </summary>
    /// <param name="child">The completed child effect group with its actual parent reference and recipient state.</param>
    /// <param name="groups">The currently open effect groups keyed by source group identity.</param>
    private static void MergeChildItemGroup(JsonObject child, JsonObject groups)
    {
        string? parentId = child[CollectionItemGroupKeys.Parent]?.GetValue<string>();
        if (parentId is null || groups[parentId] is not JsonObject parent || child[_recipientsKey] is not JsonObject children)
            return;

        JsonObject recipients = EvidenceObject(parent, _recipientsKey);
        foreach ((string id, JsonNode? value) in children)
        {
            if (value is not JsonObject sourceRow || sourceRow[CollectionTokens.RecipientEffects] is not JsonObject source)
                continue;

            JsonObject row = EvidenceObject(recipients, id);
            row[CollectionTokens.Recipient] ??= sourceRow[CollectionTokens.Recipient]?.DeepClone();
            JsonObject target = EvidenceObject(row, CollectionTokens.RecipientEffects);
            foreach (string key in new[] { CollectionTokens.HpActualTotal, CollectionTokens.PpActualTotal, CollectionTokens.HpCapExcessTotal })
            {
                if (source[key] is not null)
                    AddExact(target, key, Element(source[key]!));
            }

            if (source[CollectionTokens.HpCapExcessKnown] is JsonValue known)
                target[CollectionTokens.HpCapExcessKnown] = (target[CollectionTokens.HpCapExcessKnown] is null || target[CollectionTokens.HpCapExcessKnown]!.GetValue<bool>()) && known.GetValue<bool>();

            foreach (string key in new[] { CollectionTokens.MajorCuredStatusIds, CollectionTokens.VolatileCuredEffectIds })
            {
                if (source[key] is JsonArray members)
                {
                    foreach (JsonNode? member in members)
                    {
                        if (member is not null)
                            AddMember(target, key, Element(member));
                    }
                }
            }

            foreach ((string key, JsonNode? node) in EvidenceObject(source, CollectionItemTransactionKeys.PpSourceBuckets))
            {
                if (node is not JsonObject sourceBucket)
                    continue;

                JsonObject targetBucket = EvidenceObject(EvidenceObject(target, CollectionItemTransactionKeys.PpSourceBuckets), key);
                targetBucket[CollectionItemTransactionKeys.Source] ??= sourceBucket[CollectionItemTransactionKeys.Source]?.DeepClone();
                AddExact(targetBucket, _actualTotalKey, Element(sourceBucket[_actualTotalKey]!));
            }

            if (source[CollectionTokens.HealingSourceBuckets] is not JsonObject sourceBuckets)
                continue;

            JsonObject targetBuckets = EvidenceObject(target, CollectionTokens.HealingSourceBuckets);
            foreach ((string key, JsonNode? node) in sourceBuckets)
            {
                if (node is not JsonObject sourceBucket)
                    continue;

                if (targetBuckets[key] is not JsonObject targetBucket)
                {
                    targetBuckets[key] = sourceBucket.DeepClone();
                    continue;
                }

                foreach (string amount in new[] { CollectionTokens.ActualTotal, CollectionTokens.CapTotal })
                {
                    if (sourceBucket[amount] is not null)
                        AddExact(targetBucket, amount, Element(sourceBucket[amount]!));
                }

                foreach (string flag in new[] { CollectionTokens.ActualKnown, CollectionTokens.CapKnown })
                    targetBucket[flag] = targetBucket[flag]?.GetValue<bool>() == true && sourceBucket[flag]?.GetValue<bool>() == true;

                if (!JsonNode.DeepEquals(targetBucket[CollectionTokens.HealingAmountKind], sourceBucket[CollectionTokens.HealingAmountKind]))
                    targetBucket[CollectionTokens.HealingAmountKind] = CollectionTokens.Mixed;
            }
        }
    }
}

/// <summary>
/// Names the source-defined group relationship.
/// </summary>
internal static class CollectionItemGroupKeys
{
    internal const string Parent = CollectionItemTransactionKeys.ParentEffectGroupId;
}
