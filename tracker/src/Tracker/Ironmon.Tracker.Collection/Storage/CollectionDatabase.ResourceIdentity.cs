using System.Text.Json;
using Ironmon.Tracker.Collection.Queries.Definitions;
using Ironmon.Tracker.Collection.Storage.Encoding;

namespace Ironmon.Tracker.Collection.Storage;

/// <summary>
/// Retains source-defined item identities, immutable parent edges and current locations without an event archive.
/// </summary>
internal sealed partial class CollectionDatabase
{
    private const string _resourceReference = "resource_ref";
    private const string _resourceParents = "parent_resource_refs";
    private const string _resourceOrigin = "acquisition_origin";
    private const string _resourceController = "current_controller";
    private const string _resourceLocation = "location_kind";
    private const string _resourceLocationRef = "location_ref";
    private const string _resourceRepresentedQuantity = "represented_quantity";
    private const string _resourceIdentityNamespace = "source_item_resource";
    private const string _resourceOriginNamespace = "item_acquisition_origin";
    private const string _resourceControllerNamespace = "resource_controller";
    private const string _resourceLocationNamespace = "resource_location";
    private const string _resourceMetadataKind = "item_identity_metadata";
    private const string _resourceRegisteredEvent = "item.provenance_registered";
    private const string _resourceAssignedEvent = "held_item.assignment_resolved";
    private const string _resourceDisposedEvent = "item.disposition_resolved";
    private const string _resourcePolicy = "identity_policy";
    private const string _resourceQuality = "provenance_quality";
    private const string _resourceEntryOwner = "battle_entry_owner";

    /// <summary>
    /// Upserts an item's identity state and exact parent references atomically with its source receipt.
    /// </summary>
    /// <param name="occurrence">The authoritative event envelope, including its attempt, battle and delivery context.</param>
    /// <param name="stream">The producer stream UUID in its 16-byte database representation.</param>
    /// <param name="sequence">The contiguous source sequence being committed with this evidence.</param>
    private void RetainResourceIdentity(JsonElement occurrence, byte[] stream, ulong sequence)
    {
        string type = RequiredString(occurrence, _typeProperty);
        if (type is not (_resourceRegisteredEvent or _resourceAssignedEvent or _resourceDisposedEvent))
            return;

        JsonElement payload = occurrence.GetProperty(_payloadProperty);
        if (!payload.TryGetProperty(_resourceReference, out JsonElement resource) || resource.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || !payload.TryGetProperty(_itemIdProperty, out JsonElement item) || item.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return;

        long identity = Identity(_resourceIdentityNamespace, resource);
        long quantity = payload.TryGetProperty(_resourceRepresentedQuantity, out JsonElement actual) && actual.ValueKind == JsonValueKind.Number ? actual.GetInt64() : payload.TryGetProperty(_quantityProperty, out JsonElement original) && original.ValueKind == JsonValueKind.Number ? original.GetInt64() : 1;
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);
        string[] metadataFields = [_resourcePolicy, _resourceQuality, _resourceEntryOwner, _resourceParents];
        Dictionary<string, JsonElement> metadata = [];
        foreach (string key in metadataFields)
        {
            if (payload.TryGetProperty(key, out JsonElement value))
                metadata[key] = value;
        }

        long block = Block(_resourceMetadataKind, CollectionEncoding.Canonical(metadata));
        Dictionary<string, JsonElement> location = [];
        if (payload.TryGetProperty(_resourceLocation, out JsonElement kind))
            location[_resourceLocation] = kind;

        if (payload.TryGetProperty(_resourceLocationRef, out JsonElement reference))
            location[_resourceLocationRef] = reference;

        CollectionQueries.SaveResourceLot(_db, _transaction, identity, Identity(_itemNamespace, item), OptionalIdentity(payload, _resourceOrigin, _resourceOriginNamespace), OptionalIdentity(payload, _resourceController, _resourceControllerNamespace), Identity(_resourceLocationNamespace, JsonSerializer.SerializeToElement(location)), quantity, block, stream, CollectionEncoding.Revision(sequence), CollectionEncoding.Revision(_revision));
        if (!payload.TryGetProperty(_resourceParents, out JsonElement parents) || parents.ValueKind != JsonValueKind.Array)
            return;

        int ordinal = 0;
        foreach (JsonElement parent in parents.EnumerateArray())
            CollectionQueries.SaveResourceParent(_db, _transaction, identity, ordinal++, Identity(_resourceIdentityNamespace, parent));
    }
}
