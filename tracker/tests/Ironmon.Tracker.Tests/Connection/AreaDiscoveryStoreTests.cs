namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies tracker-owned area discovery persistence and idempotency.
/// </summary>
public sealed class AreaDiscoveryStoreTests
{
    /// <summary>
    /// Initializes area discovery store tests.
    /// </summary>
    public AreaDiscoveryStoreTests()
    {
    }

    /// <summary>
    /// Verifies that a persisted package survives reconstruction and duplicate delivery.
    /// </summary>
    [Fact]
    public void PackagePersistsBeforeIdempotentAcknowledgment()
    {
        string root = CreateRoot();
        AreaDiscoveryPackagePayload package = new()
        {
            PackageId = "package-1",
            AreaId = "area:10",
            Category = AreaContentCategory.Encounter,
            EntryKeys = ["encounter:10:0:Land:2"]
        };

        AreaDiscoveryStore store = new(new TrackerKnowledgeOptions(root));

        Assert.True(store.RecordPackage("run-1", package));
        Assert.True(store.RecordPackage("run-1", package));
        Assert.Equal(1, store.GetRevision("run-1"));

        AreaDiscoveryStore restored = new(new TrackerKnowledgeOptions(root));
        Assert.Equal("encounter:10:0:Land:2", Assert.Single(restored.GetKeys("run-1", "area:10", AreaContentCategory.Encounter)));
        Assert.Equal(1, restored.GetRevision("run-1"));
    }

    /// <summary>
    /// Verifies that newly complete detail entries and base-game completion state are persisted.
    /// </summary>
    [Fact]
    public void CompleteDetailEntriesArePersistedAndReconciled()
    {
        string root = CreateRoot();
        AreaDiscoveryStore store = new(new TrackerKnowledgeOptions(root));
        AreaLookupDetailResponsePayload response = new()
        {
            AreaId = "area:10",
            Name = "Route 102",
            Category = AreaContentCategory.Trainer,
            Trainers =
            [
                new AreaTrainerEntryPayload
                {
                    EntryId = "trainer:10:YOUNGSTER:Allen",
                    MapId = 10,
                    TrainerType = "Youngster",
                    TrainerName = "Allen",
                    PartySize = 1,
                    Defeated = true,
                    DetailsRevealed = true,
                    Party = [new AreaTrainerPokemonPayload { Slot = 1, SpeciesId = "CARVANHA:0", SpeciesName = "Carvanha", Level = 4 }]
                }
            ]
        };

        Assert.True(store.RecordDetails("run-details", response));
        Assert.Equal("trainer:10:YOUNGSTER:Allen", Assert.Single(store.GetKeys("run-details", "area:10", AreaContentCategory.Trainer)));

        string path = Path.Combine(root, TrackerStorageNames.RunsDirectory, "run-details", TrackerStorageNames.AreaDiscoveriesFile);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement trainers = document.RootElement.GetProperty("trainers");
        Assert.Equal("Carvanha", trainers.GetProperty("trainer:10:YOUNGSTER:Allen").GetProperty("party")[0].GetProperty("species_name").GetString());
    }

    /// <summary>
    /// Verifies that archived reconstruction cannot replace persisted run details and cannot invent legacy item mappings.
    /// </summary>
    [Fact]
    public void ArchivedDetailsRestorePersistedEntriesAndHideUnknownLegacyItems()
    {
        AreaDiscoveryStore store = new(new TrackerKnowledgeOptions(CreateRoot()));
        AreaLookupDetailResponsePayload persisted = new()
        {
            AreaId = "area:10",
            Name = "Route 102",
            Category = AreaContentCategory.Item,
            Items =
            [
                new AreaItemEntryPayload
                {
                    EntryId = "item:10:1",
                    MapId = 10,
                    Kind = "visible",
                    DetailsRevealed = true,
                    Items = [new AreaItemIdentityPayload { ItemId = "POTION", ItemName = "Potion" }]
                }
            ]
        };
        Assert.True(store.RecordDetails("run-archive", persisted));

        AreaLookupDetailResponsePayload reconstructed = new()
        {
            AreaId = "area:10",
            Name = "Route 102",
            Category = AreaContentCategory.Item,
            Items =
            [
                new AreaItemEntryPayload
                {
                    EntryId = "item:10:1",
                    MapId = 10,
                    Kind = "visible",
                    DetailsRevealed = true,
                    Items = [new AreaItemIdentityPayload { ItemId = "MASTERBALL", ItemName = "Master Ball" }]
                },
                new AreaItemEntryPayload
                {
                    EntryId = "item:10:2",
                    MapId = 10,
                    Kind = "hidden",
                    Hidden = true,
                    DetailsRevealed = true,
                    Items = [new AreaItemIdentityPayload { ItemId = "RARECANDY", ItemName = "Rare Candy" }]
                }
            ]
        };

        AreaLookupDetailResponsePayload restored = store.RestoreArchivedDetails("run-archive", reconstructed, false);

        Assert.Equal("POTION", restored.Items[0].Items[0].ItemId);
        Assert.False(restored.Items[1].DetailsRevealed);
        Assert.Empty(restored.Items[1].Items);
    }

    /// <summary>
    /// Creates an isolated persistence root.
    /// </summary>
    /// <returns>The isolated directory path.</returns>
    private static string CreateRoot()
        => Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
}
