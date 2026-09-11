namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies tracker-owned area discovery persistence and idempotency.
/// </summary>
public sealed class AreaDiscoveryStoreTests : IDisposable
{
    private const string _battleRun = "run-battle-details";
    private const string _battleSpecies = "CARVANHA:0";
    private const string _otherBattleSpecies = "SHARPEDO:0";
    private const string _battleSpeciesName = "Carvanha";
    private const string _battleAbility = "ROUGH SKIN";
    private const string _battleMove = "BITE";
    private const string _battleType = "DARK";
    private const string _battleEffect = "May cause flinching.";
    private const string _changedBattleEffect = "Updated effect.";
    private readonly List<string> _roots = [];

    /// <summary>
    /// Restores missing archive data without transferring a battle set between different party members.
    /// </summary>
    /// <param name="slot">The reconstructed party slot.</param>
    /// <param name="sameSpecies">Whether the reconstructed identity matches the recorded member.</param>
    /// <param name="level">The reconstructed level.</param>
    /// <param name="expected">Whether the reconstructed set is compatible.</param>
    [Theory]
    [InlineData(1, true, 4, true)]
    [InlineData(2, true, 4, false)]
    [InlineData(1, false, 4, false)]
    [InlineData(1, true, 5, false)]
    public void ArchiveRestoresMissingTrainerBattleDetailsOnlyForMatchingMembers(int slot, bool sameSpecies, int level, bool expected)
    {
        string root = CreateRoot();
        AreaDiscoveryStore store = new(new TrackerKnowledgeOptions(root));
        Assert.True(store.RecordDetails(_battleRun, CreateBattleResponse(false, false)));
        AreaDiscoveryStore reloaded = new(new TrackerKnowledgeOptions(root));
        AreaLookupDetailResponsePayload response = reloaded.RestoreArchivedDetails(_battleRun, CreateBattleResponse(true, true, slot, sameSpecies, level), false);
        AreaTrainerEntryPayload trainer = Assert.Single(response.Trainers);
        AreaTrainerPokemonPayload member = Assert.Single(trainer.Party);
        Assert.True(trainer.Defeated);
        Assert.Equal(_battleSpecies, member.SpeciesId);
        Assert.Equal(4, member.Level);
        Assert.Equal(1, member.Slot);
        Assert.Equal(expected, member.AbilitiesRevealed);
        Assert.Equal(expected, member.MovesRevealed);
        Assert.Equal(expected ? 1 : 0, member.Abilities.Count);
        Assert.Equal(expected ? 1 : 0, member.Moves.Count);
    }

    /// <summary>
    /// Keeps recorded battle details authoritative while supplementing independently missing information.
    /// </summary>
    [Fact]
    public void ArchivePreservesRecordedMovesWhileRestoringAbilities()
    {
        AreaDiscoveryStore store = new(new TrackerKnowledgeOptions(CreateRoot()));
        Assert.True(store.RecordDetails(_battleRun, CreateBattleResponse(false, true)));
        AreaLookupDetailResponsePayload response = store.RestoreArchivedDetails(_battleRun, CreateBattleResponse(true, true, effect: _changedBattleEffect), false);
        AreaTrainerPokemonPayload member = Assert.Single(Assert.Single(response.Trainers).Party);
        Assert.True(member.AbilitiesRevealed);
        Assert.Equal(_battleAbility, Assert.Single(member.Abilities).Id);
        Assert.Equal(_battleEffect, Assert.Single(member.Moves).Description);
    }

    /// <summary>
    /// Persists newly disclosed battle fields and effect changes without rewriting equivalent entries.
    /// </summary>
    [Fact]
    public void TrainerBattleDetailChangesAdvanceTheStoredRevision()
    {
        string root = CreateRoot();
        AreaDiscoveryStore store = new(new TrackerKnowledgeOptions(root));
        Assert.True(store.RecordDetails(_battleRun, CreateBattleResponse(false, false)));
        Assert.True(store.RecordDetails(_battleRun, CreateBattleResponse(true, false)));
        Assert.True(store.RecordDetails(_battleRun, CreateBattleResponse(true, true)));
        Assert.False(store.RecordDetails(_battleRun, CreateBattleResponse(true, true)));
        Assert.True(store.RecordDetails(_battleRun, CreateBattleResponse(true, true, effect: _changedBattleEffect)));
        Assert.Equal(4, store.GetRevision(_battleRun));
        AreaDiscoveryStore reloaded = new(new TrackerKnowledgeOptions(root));
        AreaLookupDetailResponsePayload response = reloaded.RestoreArchivedDetails(_battleRun, CreateBattleResponse(false, false), false);
        Assert.Equal(_changedBattleEffect, Assert.Single(Assert.Single(Assert.Single(response.Trainers).Party).Moves).Description);
    }

    /// <summary>
    /// Creates a trainer response with independently disclosed battle details.
    /// </summary>
    /// <param name="abilities">Whether abilities are disclosed.</param>
    /// <param name="moves">Whether moves are disclosed.</param>
    /// <param name="slot">The member's slot.</param>
    /// <param name="sameSpecies">Whether to use the original species.</param>
    /// <param name="level">The member's level.</param>
    /// <param name="effect">The move description.</param>
    /// <returns>A complete trainer response containing the requested member.</returns>
    private static AreaLookupDetailResponsePayload CreateBattleResponse(bool abilities, bool moves, int slot = 1, bool sameSpecies = true, int level = 4, string effect = _battleEffect)
    {
        AreaLookupDetailResponsePayload original = CreateTrainerResponse(_battleSpeciesName);
        AreaTrainerEntryPayload trainer = Assert.Single(original.Trainers);
        return new AreaLookupDetailResponsePayload
        {
            AreaId = original.AreaId, Name = original.Name, Category = original.Category,
            Trainers = [new()
            {
                EntryId = trainer.EntryId, TrainerType = trainer.TrainerType, TrainerName = trainer.TrainerName,
                MapId = trainer.MapId, PartySize = 1, DetailsRevealed = true, Defeated = true,
                Party = [new()
                {
                    Slot = slot, SpeciesId = sameSpecies ? _battleSpecies : _otherBattleSpecies, SpeciesName = _battleSpeciesName, Level = level,
                    AbilitiesRevealed = abilities, MovesRevealed = moves,
                    Abilities = abilities ? [new() { Id = _battleAbility, Name = _battleAbility, Description = _battleEffect }] : [],
                    Moves = moves ? [new() { Id = _battleMove, Name = _battleMove, Type = _battleType, Category = MoveCategory.Physical, Description = effect }] : []
                }]
            }]
        };
    }

    /// <summary>
    /// Initializes area discovery store tests.
    /// </summary>
    public AreaDiscoveryStoreTests()
    {
    }

    /// <summary>
    /// Removes every temporary discovery root owned by the current test.
    /// </summary>
    public void Dispose()
    {
        foreach (string root in _roots)
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
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
    /// Verifies each encountered fusion contributes once to the matching mechanic's progress.
    /// </summary>
    [Fact]
    public void FusionDiscoveryCountsOnlyTheDisplayedEncounterMode()
    {
        AreaDiscoveryStore store = new(new TrackerKnowledgeOptions(CreateRoot()));
        AreaDiscoveryPackagePayload package = new()
        {
            PackageId = "package-fusions",
            AreaId = "area:10",
            Category = AreaContentCategory.Encounter,
            EntryKeys =
            [
                "encounter:10:0:Land:1",
                "encounter_fusion:10:overworld_cross:0:Land:1:0:Water:2",
                "encounter_fusion:10:standard_cross:0:Land:1:0:Water:2",
                "encounter_fusion:10:standard_same:0:Land:1:0:Land:2"
            ]
        };

        Assert.True(store.RecordPackage("run-fusions", package));
        Assert.True(store.RecordPackage("run-fusions", package));
        Assert.Equal(4, store.GetKeys("run-fusions", "area:10", AreaContentCategory.Encounter).Count);
        Assert.Equal(4, store.GetCount("run-fusions", "area:10", AreaContentCategory.Encounter));
        Assert.Equal(3, store.GetCount("run-fusions", "area:10", AreaContentCategory.Encounter, false));
        Assert.Equal(2, store.GetCount("run-fusions", "area:10", AreaContentCategory.Encounter, true));
        Assert.Equal((1, 3), store.GetEncounterCounts("run-fusions", "area:10"));
        Assert.Equal((1, 2), store.GetEncounterCounts("run-fusions", "area:10", false));
        Assert.Equal((1, 1), store.GetEncounterCounts("run-fusions", "area:10", true));
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
    /// Verifies that a failed detail save leaves the live document and revision unchanged.
    /// </summary>
    [Fact]
    public void FailedDetailPersistenceDoesNotPublishOrRetainChanges()
    {
        string root = CreateRoot();
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        File.WriteAllText(root, "persistence root collision");
        try
        {
            AreaDiscoveryStore store = new(new TrackerKnowledgeOptions(root));
            int changes = 0;
            store.Changed += (_, _) => changes++;
            AreaLookupDetailResponsePayload response = new()
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
                        Collected = true,
                        DetailsRevealed = true,
                        Items = [new AreaItemIdentityPayload { ItemId = "POTION", ItemName = "Potion" }]
                    }
                ]
            };

            Assert.False(store.RecordDetails("run-failed-save", response));
            Assert.Equal(0, changes);
            Assert.Equal(0, store.GetRevision("run-failed-save"));
            Assert.Empty(store.GetKeys("run-failed-save", "area:10", AreaContentCategory.Item));
            Assert.NotNull(store.LastError);
        }
        finally
        {
            File.Delete(root);
        }
    }

    /// <summary>
    /// Verifies equivalent deep detail payloads are ignored while nested changes advance the revision.
    /// </summary>
    [Fact]
    public void EquivalentDetailPayloadsDoNotAdvanceRevision()
    {
        string root = CreateRoot();
        try
        {
            AreaDiscoveryStore store = new(new TrackerKnowledgeOptions(root));
            int changes = 0;
            store.Changed += (_, _) => changes++;

            Assert.True(store.RecordDetails("run-equivalence", CreateTrainerResponse("Carvanha")));
            Assert.False(store.RecordDetails("run-equivalence", CreateTrainerResponse("Carvanha")));
            Assert.True(store.RecordDetails("run-equivalence", CreateTrainerResponse("Sharpedo")));

            Assert.True(store.RecordDetails("run-equivalence", CreateEncounterResponse("Ralts")));
            Assert.False(store.RecordDetails("run-equivalence", CreateEncounterResponse("Ralts")));
            Assert.True(store.RecordDetails("run-equivalence", CreateEncounterResponse("Kirlia")));

            Assert.True(store.RecordDetails("run-equivalence", CreateItemResponse("Potion")));
            Assert.False(store.RecordDetails("run-equivalence", CreateItemResponse("Potion")));
            Assert.True(store.RecordDetails("run-equivalence", CreateItemResponse("Super Potion")));

            Assert.Equal(6, store.GetRevision("run-equivalence"));
            Assert.Equal(6, changes);
        }
        finally
        {
            Directory.Delete(root, true);
        }
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
    /// Creates trainer details with one nested species name.
    /// </summary>
    /// <param name="speciesName">The disclosed species name.</param>
    /// <returns>The trainer detail response.</returns>
    private static AreaLookupDetailResponsePayload CreateTrainerResponse(string speciesName) => new()
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
                Party = [new AreaTrainerPokemonPayload { Slot = 1, SpeciesId = "CARVANHA:0", SpeciesName = speciesName, Level = 4 }]
            }
        ]
    };

    /// <summary>
    /// Creates encounter details with one disclosed species name.
    /// </summary>
    /// <param name="speciesName">The disclosed species name.</param>
    /// <returns>The encounter detail response.</returns>
    private static AreaLookupDetailResponsePayload CreateEncounterResponse(string speciesName) => new()
    {
        AreaId = "area:10",
        Name = "Route 102",
        Category = AreaContentCategory.Encounter,
        Encounters =
        [
            new AreaEncounterEntryPayload
            {
                EntryId = "encounter:10:0:Land:1",
                MapId = 10,
                EncounterType = "Land",
                Slot = 1,
                ProbabilityPercent = 20,
                MinimumLevel = 3,
                MaximumLevel = 4,
                Encountered = true,
                DetailsRevealed = true,
                SpeciesId = "RALTS:0",
                SpeciesName = speciesName
            }
        ]
    };

    /// <summary>
    /// Creates item details with one nested item name.
    /// </summary>
    /// <param name="itemName">The disclosed item name.</param>
    /// <returns>The item detail response.</returns>
    private static AreaLookupDetailResponsePayload CreateItemResponse(string itemName) => new()
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
                X = 4,
                Y = 5,
                Kind = "visible",
                Collected = true,
                DetailsRevealed = true,
                Items = [new AreaItemIdentityPayload { ItemId = "POTION", ItemName = itemName }]
            }
        ]
    };

    /// <summary>
    /// Creates an isolated persistence root.
    /// </summary>
    /// <returns>The isolated directory path.</returns>
    private string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "IronmonTrackerTests", Guid.NewGuid().ToString("N"));
        _roots.Add(root);
        return root;
    }
}
