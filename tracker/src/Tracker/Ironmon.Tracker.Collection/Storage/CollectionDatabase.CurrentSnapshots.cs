using System.Text.Json;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Replaces complete current inventory snapshots without turning them into historical unions.
/// </summary>
internal sealed partial class CollectionDatabase
{
    private const string _inventorySnapshotEvent = "inventory.snapshot_reconciled";
    private const string _inventoryCurrentFamily = "inventory_item_quantity_current";
    private const string _itemQuantitiesProperty = "item_quantities";
    private const string _containerProperty = "container_ref";
    private const string _quantityProperty = "quantity";
    private const string _heldAssignmentsProperty = "held_assignments";
    private const string _lineageCompleteProperty = "lineage_complete";
    private const string _inventoryLotsProperty = "item_lots";
    private const string _containerNamespace = "inventory_container";

    /// <summary>
    /// Replaces complete current Bag projections while retaining a known-empty snapshot explicitly.
    /// </summary>
    /// <param name="occurrence">The authoritative event envelope, including its attempt, battle and delivery context.</param>
    /// <param name="stream">The producer stream UUID in its 16-byte database representation.</param>
    /// <param name="sequence">The contiguous source sequence being committed with this evidence.</param>
    private void ReconcileCurrentInventory(JsonElement occurrence, byte[] stream, ulong sequence)
    {
        if (RequiredString(occurrence, _typeProperty) != _inventorySnapshotEvent)
            return;

        JsonElement payload = occurrence.GetProperty(_payloadProperty);
        if (!payload.TryGetProperty(_itemQuantitiesProperty, out JsonElement quantities) || quantities.ValueKind != JsonValueKind.Array || !payload.TryGetProperty(_containerProperty, out JsonElement container) || container.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return;

        foreach (JsonElement item in quantities.EnumerateArray())
        {
            if (!item.TryGetProperty(_itemIdProperty, out JsonElement identity) || identity.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || !item.TryGetProperty(_quantityProperty, out JsonElement quantity) || quantity.ValueKind is not (JsonValueKind.Number or JsonValueKind.String) || ExactInteger(quantity) < 0)
                return;
        }

        int family = _families[_inventoryCurrentFamily].Id;
        long scope = Scope(_currentSaveScope, _epoch!, null);
        if (CollectionQueries.ReadHotPartition(_db, _transaction, scope, family) is { } partition)
            CollectionQueries.DeletePartitionSeries(_db, _transaction, partition.Id);

        CollectionQueries.DeleteOrderedState(_db, _transaction, scope, family);
        byte[] state = CollectionEncoding.Canonical(new { item_quantities = quantities, held_assignments = payload.TryGetProperty(_heldAssignmentsProperty, out JsonElement held) ? held : (JsonElement?)null, lineage_complete = payload.TryGetProperty(_lineageCompleteProperty, out JsonElement complete) ? complete : (JsonElement?)null, item_lots = payload.TryGetProperty(_inventoryLotsProperty, out JsonElement lots) ? lots : (JsonElement?)null });
        CollectionQueries.SaveCurrentInventory(_db, _transaction, family, scope, Identity(_containerNamespace, container), CollectionEncoding.Compress(state), stream, CollectionEncoding.Revision(sequence), CollectionEncoding.Revision(_revision));
    }
}
