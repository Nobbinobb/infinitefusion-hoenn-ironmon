namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies tracker battle-item inventory and action payload contracts.
/// </summary>
public sealed class BattleItemPayloadTests
{
    /// <summary>
    /// Initializes the battle-item payload tests.
    /// </summary>
    public BattleItemPayloadTests()
    {
    }

    /// <summary>
    /// Verifies item categories and move targeting use the protocol's snake-case representation.
    /// </summary>
    [Fact]
    public void BattleItemRequestAndSnapshotUseCanonicalJson()
    {
        BattleItemSnapshot item = new()
        {
            Id = "MAXETHER",
            Name = "Max Ether",
            Description = "Fully restores one move's PP.",
            Quantity = 2,
            Category = BattleItemCategory.PpRestore,
            RequiresMove = true
        };

        BattleItemUseRequestPayload request = new() { ItemId = item.Id, MoveIndex = 2, TargetPosition = 1 };
        string itemJson = TrackerJson.SerializePayload(item).GetRawText();
        string requestJson = TrackerJson.SerializePayload(request).GetRawText();

        Assert.Contains("\"category\":\"pp_restore\"", itemJson, StringComparison.Ordinal);
        Assert.Contains("\"requires_move\":true", itemJson, StringComparison.Ordinal);
        Assert.Contains("\"move_index\":2", requestJson, StringComparison.Ordinal);
        Assert.Contains("\"target_position\":1", requestJson, StringComparison.Ordinal);
    }
}
