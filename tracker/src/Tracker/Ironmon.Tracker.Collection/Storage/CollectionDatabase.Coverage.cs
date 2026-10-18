using System.Globalization;
using System.Text.Json;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Retains explicit interrupted coverage boundaries instead of fabricating a complete crash tail.
/// </summary>
internal sealed partial class CollectionDatabase
{
    private const string _retirementKindProperty = "retirement_kind";
    private const string _interruptedRetirement = "recovered_interrupted";
    private const string _interruptedTailReason = "producer_interrupted_tail";
    private const string _recoveryGapSuffix = "/interrupted-tail";

    /// <summary>
    /// Records an unknown unflushed crash tail under the actual predecessor, once per receipt.
    /// </summary>
    /// <param name="occurrence">The authoritative event envelope, including its attempt, battle and delivery context.</param>
    private void RecordRecoveryGap(JsonElement occurrence)
    {
        JsonElement payload = occurrence.GetProperty(_payloadProperty);
        if (!payload.TryGetProperty(_retirementKindProperty, out JsonElement retirement) || retirement.GetString() != _interruptedRetirement || !payload.TryGetProperty(_predecessorProperty, out JsonElement predecessor) || predecessor.ValueKind != JsonValueKind.String)
            return;

        byte[] stream = CollectionEncoding.Identifier(predecessor.GetString()!);
        ulong? firstMissing = null;
        if (payload.TryGetProperty(_predecessorSequenceProperty, out JsonElement sequence) && sequence.ValueKind == JsonValueKind.String && ulong.TryParse(sequence.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong recovered))
        {
            ulong frontier = Math.Max(recovered, Receipt(stream));
            if (frontier < ulong.MaxValue)
                firstMissing = frontier + 1;
        }

        byte[] id = CollectionEncoding.DerivedIdentifier(RequiredString(_currentBatch, _streamProperty) + _recoveryGapSuffix);
        byte[] detail = CollectionEncoding.Canonical(payload);
        CollectionQueries.InsertCoverageGap(_db, _transaction, id, _lifetimeScope, stream, firstMissing.HasValue ? CollectionEncoding.Revision(firstMissing.Value) : null, _interruptedTailReason, Now(), detail);
        foreach (Reduction.CollectionFamilyDefinition family in _catalogue.Families)
            CollectionQueries.AddCoverageGapFamily(_db, _transaction, id, family.Id);

        CollectionQueries.MarkInterruptedRetirement(_db, _transaction, stream, detail);
        CollectionQueries.AddRetirementGap(_db, _transaction, stream, id);
    }
}
