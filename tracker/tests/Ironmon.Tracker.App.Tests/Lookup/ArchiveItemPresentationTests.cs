using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.App.Components.Lookup;
using Ironmon.Tracker.Protocol.Lookup;

namespace Ironmon.Tracker.App.Tests.Lookup;

/// <summary>
/// Verifies item usage is aggregated independently of pickup progress and retains historical identifiers.
/// </summary>
public sealed class ArchiveItemPresentationTests
{
    private const string _lost = "lost";
    private const string _bag = "Bag";
    private const string _held = "Held";
    private const string _potion = "POTION";
    private const string _ether = "ETHER";
    private const string _unknown = "FUTURE_ITEM";

    /// <summary>
    /// Verifies repeated items across consumption sources have one combined count and a catalog-derived icon.
    /// </summary>
    [Fact]
    public void UsageCombinesSourcesWithoutChangingStoredStatistics()
    {
        RunStatisticsPayload statistics = new()
        {
            Result = _lost,
            ItemsBySource = new Dictionary<string, Dictionary<string, int>>
            {
                [_bag] = new() { [_potion] = 3, [_ether] = 2 },
                [_held] = new() { [_potion] = 4 }
            }
        };

        IReadOnlyList<ArchivedItemUsage> items = ArchiveItemPresentation.GetUsage(statistics);
        Assert.Equal(2, items.Count);
        Assert.Equal(7, items[0].Count);
        Assert.Equal(ObsidianIconKind.HeartPulse, ItemCategoryPresentation.GetIcon(items[0].Category));
        Assert.Equal(ObsidianIconKind.Pill, ItemCategoryPresentation.GetIcon(items[1].Category));
        Assert.Equal(3, statistics.ItemsBySource[_bag][_potion]);
        Assert.Equal(4, statistics.ItemsBySource[_held][_potion]);
    }

    /// <summary>
    /// Verifies unknown historical identifiers remain visible and large source totals do not overflow.
    /// </summary>
    [Fact]
    public void UnknownItemsRetainTheirIdentityAndCombinedCount()
    {
        RunStatisticsPayload statistics = new()
        {
            Result = _lost,
            ItemsBySource = new Dictionary<string, Dictionary<string, int>>
            {
                [_bag] = new() { [_unknown] = int.MaxValue },
                [_held] = new() { [_unknown] = 1 }
            }
        };

        ArchivedItemUsage item = Assert.Single(ArchiveItemPresentation.GetUsage(statistics));
        Assert.Equal(_unknown, item.Name);
        Assert.Equal((long)int.MaxValue + 1, item.Count);
        Assert.Equal(ObsidianIconKind.Backpack, ItemCategoryPresentation.GetIcon(item.Category));
    }
}
