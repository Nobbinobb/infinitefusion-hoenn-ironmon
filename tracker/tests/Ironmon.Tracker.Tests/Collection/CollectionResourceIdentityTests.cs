using Ironmon.Tracker.Collection;
using System.Text.Json;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Checks exact resource splits, consumption, recovery edges and receipt durability.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const string _resourceEvent = "item.provenance_registered";
    private const string _resourceDisposal = "item.disposition_resolved";
    private const string _resourceOrigin = "shop";
    private const string _resourceParent = "lot-parent";
    private const string _resourceChild = "unit-child";
    private const string _resourceRecovered = "unit-recovered";
    private const string _resourcePolicy = "source_unit_fifo_v1";
    private const string _resourceQuality = "exact_lot";
    private const string _resourceCountSql = "SELECT COUNT(*) FROM collection_resource_lots";
    private const string _resourceQuantitySql = "SELECT SUM(current_quantity) FROM collection_resource_lots";
    private const string _resourceEdgesSql = "SELECT COUNT(*) FROM collection_resource_parents p JOIN collection_resource_lots l ON l.resource_identity_id=p.parent_identity_id";

    /// <summary>
    /// Exercises a real split, consume and recover chain through close, retry and batch rollback.
    /// </summary>
    /// <returns>A task that completes after the asynchronous fixture operations and all described assertions finish.</returns>
    [Fact]
    public async Task ResourceLineageAndCurrentQuantitiesSurviveReopenRetryAndRollback()
    {
        using TestDirectory directory = new();
        JsonElement batch = Batch(_stream, [Occurrence(_resourceEvent, Resource(_resourceParent, 3), 1), Occurrence(_resourceEvent, Resource(_resourceParent, 2), 2), Occurrence(_resourceEvent, Resource(_resourceChild, 1, _resourceParent), 3), Occurrence(_resourceDisposal, Resource(_resourceChild, 0, _resourceParent), 4)]);
        await using (GameplayCollectionStore store = new(directory.Path))
            await store.AcceptBatchAsync(batch);

        await using GameplayCollectionStore reopened = new(directory.Path);
        await reopened.AcceptBatchAsync(batch);
        Assert.Equal(2L, ReadScalar(directory.Path, _resourceCountSql));
        Assert.Equal(2L, ReadScalar(directory.Path, _resourceQuantitySql));
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.AcceptBatchAsync(Batch(_stream, [Occurrence(_resourceEvent, Resource(_resourceRecovered, 1, _resourceChild), 5), Occurrence(_resourceEvent, Resource(_resourceParent, 99), 7)])));
        Assert.Equal(2L, ReadScalar(directory.Path, _resourceCountSql));
        Assert.Equal(2L, ReadScalar(directory.Path, _resourceQuantitySql));
        await reopened.AcceptBatchAsync(Batch(_stream, [Occurrence(_resourceEvent, Resource(_resourceRecovered, 1, _resourceChild), 5)]));
        Assert.Equal(3L, ReadScalar(directory.Path, _resourceQuantitySql));
        Assert.Equal(2L, ReadScalar(directory.Path, _resourceEdgesSql));
        AssertIntegrity(directory.Path);
    }

    /// <summary>
    /// Creates source resource identity evidence with an exact live quantity and optional parent.
    /// </summary>
    /// <param name="reference">The source-defined item unit or lot identifier.</param>
    /// <param name="quantity">The actual current quantity represented by the resource, independent of acquisition credit.</param>
    /// <param name="parent">The exact parent resource reference, or null when the resource has no parent.</param>
    /// <returns>The source resource-identity payload with current quantity and an explicit parent-reference list.</returns>
    private static JsonElement Resource(string reference, int quantity, string? parent = null)
        => JsonSerializer.SerializeToElement(new { resource_ref = reference, item_id = _potion, represented_quantity = quantity, quantity = 0, acquisition_origin = _resourceOrigin, current_controller = _player, battle_entry_owner = _player, parent_resource_refs = parent is null ? Array.Empty<string>() : new[] { parent }, identity_policy = _resourcePolicy, provenance_quality = _resourceQuality, location_kind = _playerBag });
}
