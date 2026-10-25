using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ironmon.Tracker.Collection.Reduction;

/// <summary>
/// Folds activation-wide recipient evidence independently of per-recipient measurements.
/// </summary>
public sealed partial class CollectionEventReducer
{
    /// <summary>
    /// Identifies summaries whose grain is one actual activation, including multi-recipient uses.
    /// </summary>
    /// <param name="identity">The direct summary group identifier, or null when unavailable.</param>
    /// <returns>True when the summary grain is one item activation rather than one recipient or effect.</returns>
    private static bool IsItemTransactionGroup(string? identity) => identity is CollectionItemTransactionKeys.Valuecda15dcf832b or CollectionItemTransactionKeys.Value01a8d8ba2c28 or CollectionItemTransactionKeys.Valuecfde3c383289 or CollectionItemTransactionKeys.Value773a1526bb46;

    /// <summary>
    /// Tests whether a direct view explicitly distinguishes inclusive and immediate source amounts.
    /// </summary>
    /// <param name="group">The direct summary definition whose dimensions may request separate recovery-source views.</param>
    /// <returns>True when the summary declares a source-view dimension for recovery amounts.</returns>
    private static bool HasSourceView(JsonElement group) => group.GetProperty(_dimensionsKey).EnumerateArray().Any(dimension => Text(dimension, _keyKey) == CollectionItemTransactionKeys.SourceView);

    /// <summary>
    /// Builds exact activation-level sufficient values from all actually observed recipients.
    /// </summary>
    /// <param name="payload">The completed item activation enriched in place from all observed player recipients.</param>
    private static void FinalizeItemTransaction(JsonObject payload)
    {
        JsonObject recipients = payload[_recipientsKey] as JsonObject ?? new JsonObject();
        JsonArray major = [], volatileCures = [], secondary = [];
        BigInteger hp = BigInteger.Zero, pp = BigInteger.Zero;
        bool any = false;
        foreach (JsonObject row in recipients.Select(pair => pair.Value).OfType<JsonObject>())
        {
            if (Text(Property(Element(row), CollectionTokens.Recipient), _ownerRoleKey) != _playerRole || row[CollectionTokens.RecipientEffects] is not JsonObject effects)
                continue;

            hp += ReadNodeBigInteger(effects[CollectionTokens.HpDirectItemTotal]);
            pp += ReadNodeBigInteger(effects[CollectionItemTransactionKeys.PpDirectItemTotal]);
            any |= effects[CollectionTokens.AnyEffect]?.GetValue<bool>() == true;
            foreach ((string key, JsonArray output) in new[] { (CollectionTokens.MajorCuredStatusIds, major), (CollectionTokens.VolatileCuredEffectIds, volatileCures), (CollectionTokens.HpSecondaryBySource, secondary) })
            {
                if (effects[key] is not JsonArray members)
                    continue;

                foreach (JsonNode? member in members)
                {
                    if (!output.Any(existing => JsonNode.DeepEquals(existing, member)))
                        output.Add(member?.DeepClone());
                }
            }
        }

        payload[CollectionItemTransactionKeys.RecipientEligibilityPolicy] = CollectionItemTransactionKeys.ObservedPlayerRecipientV1;
        payload[CollectionItemTransactionKeys.AnyEligibleRecipientDirectItemHpPositive] = hp > 0;
        payload[CollectionItemTransactionKeys.AnyEligiblePlayerMajorStatusCured] = major.Count > 0;
        payload[CollectionItemTransactionKeys.ActualCuredMajorStatusIds] = major;
        payload[CollectionItemTransactionKeys.VolatileCuresSeparate] = volatileCures;
        payload[CollectionItemTransactionKeys.SecondarySourceHpResults] = secondary;
        payload[CollectionItemTransactionKeys.ActivationLevelAnyEligibleRecipientSuccess] = pp > 0;
        payload[CollectionItemTransactionKeys.PositivePpActivationCount] = pp > 0 ? 1 : 0;
        payload[CollectionItemTransactionKeys.ActualDirectItemPpTotal] = pp.ToString(System.Globalization.CultureInfo.InvariantCulture);
        payload[CollectionItemTransactionKeys.ImmediateSourceKind] = _itemSourceKind;
        payload[CollectionItemTransactionKeys.EligibleRecipientEffectExists] = any;
        payload[CollectionItemTransactionKeys.InclusiveHpResult] = recipients.Select(pair => pair.Value?[CollectionTokens.RecipientEffects]?[CollectionTokens.HpResult]?.GetValue<string>()).Any(value => value is not null && value != CollectionTokens.NoGain) ? CollectionTokens.Positive : CollectionTokens.NoGain;
    }

    /// <summary>
    /// Expands source views while keeping activation summaries at one occurrence.
    /// </summary>
    /// <param name="group">The direct summary definition determining activation-wide or recovery-source projection grain.</param>
    /// <param name="frame">The immutable source views and bounded causal context for this projection.</param>
    /// <returns>One activation-wide frame or the inclusive, direct-item and secondary recovery frames as declared.</returns>
    private static IEnumerable<Frame> DirectFrames(JsonElement group, Frame frame)
    {
        if (IsItemTransactionGroup(Text(group, _idKey)))
        {
            yield return frame with { Row = frame.Payload };
            yield break;
        }
        if (!HasSourceView(group))
        {
            yield return frame;
            yield break;
        }

        foreach ((string view, string key) in new[] { (CollectionItemTransactionKeys.Inclusive, CollectionTokens.HpActualTotal), (CollectionItemTransactionKeys.DirectItem, CollectionTokens.HpDirectItemTotal), (CollectionItemTransactionKeys.Secondary, CollectionTokens.HpSecondaryTotal) })
        {
            JsonObject row = frame.Row.ValueKind == JsonValueKind.Object ? JsonNode.Parse(frame.Row.GetRawText())!.AsObject() : new JsonObject();
            JsonElement amount = Path(frame.Row, CollectionTokens.RecipientEffects + CollectionItemTransactionKeys.Dot + key);
            row[CollectionItemTransactionKeys.SourceView] = view;
            EvidenceCopy(row, _actualTotalKey, amount);
            EvidenceCopy(row, _maximumValueKey, amount);
            yield return frame with { Row = Element(row) };
        }
    }

    /// <summary>
    /// Retains immediate PP sources so secondary restoration is never credited to the initiating item.
    /// </summary>
    /// <param name="effects">The recipient's bounded effect totals and immediate-source buckets.</param>
    /// <param name="payload">The actual PP restoration with its immediate effect source and restored PP amount.</param>
    private static void ObservePpSource(JsonObject effects, JsonElement payload)
    {
        JsonElement source = Property(payload, CollectionTokens.EffectSource);
        JsonObject buckets = EvidenceObject(effects, CollectionItemTransactionKeys.PpSourceBuckets);
        string key = source.GetRawText();
        JsonObject bucket = EvidenceObject(buckets, key);
        EvidenceCopy(bucket, CollectionItemTransactionKeys.Source, source);
        AddExact(bucket, _actualTotalKey, Property(payload, CollectionTokens.ActualTotalPp));
    }

    /// <summary>
    /// Resolves direct PP only from matching immediate item evidence.
    /// </summary>
    /// <param name="effects">The recipient's bounded effect totals and immediate-source buckets.</param>
    /// <param name="initiatingId">The initiating item identity against which actual immediate PP sources are matched.</param>
    private static void FinalizePpSources(JsonObject effects, JsonElement initiatingId)
    {
        BigInteger total = BigInteger.Zero;
        foreach (JsonObject bucket in EvidenceObject(effects, CollectionItemTransactionKeys.PpSourceBuckets).Select(pair => pair.Value).OfType<JsonObject>())
        {
            JsonElement source = Property(Element(bucket), CollectionItemTransactionKeys.Source);
            JsonElement id = Property(source, CollectionTokens.CanonicalId);
            if (!Available(id))
                id = Property(source, CollectionTokens.MechanicId);

            if (Text(source, _kindKey) == _itemSourceKind && Equal(id, initiatingId))
                total += ReadNodeBigInteger(bucket[_actualTotalKey]);
        }
        effects[CollectionItemTransactionKeys.PpDirectItemTotal] = total.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
