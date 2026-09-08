namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies public area lookup payload serialization and redaction shapes.
/// </summary>
public sealed class AreaLookupPayloadTests
{
    private const string _trainerSpecies = "LANTURN:0";
    private const string _trainerSpeciesName = "Lanturn";
    private const string _trainerAbility = "VOLTABSORB";
    private const string _trainerAbilityName = "Volt Absorb";
    private const string _trainerMove = "BUBBLEBEAM";
    private const string _trainerMoveName = "Bubble Beam";
    private const string _trainerMoveType = "WATER";
    private const string _trainerMoveDescription = "May lower the target's Speed.";

    /// <summary>
    /// Keeps old peers undisclosed while preserving new trainer battle-set fields over the wire.
    /// </summary>
    [Fact]
    public void TrainerBattleDetailsRoundTripWithoutInventingLegacyAccess()
    {
        AreaTrainerPokemonPayload legacy = new() { SpeciesId = _trainerSpecies, SpeciesName = _trainerSpeciesName };
        Assert.False(legacy.AbilitiesRevealed);
        Assert.False(legacy.MovesRevealed);
        Assert.Empty(legacy.Abilities);
        Assert.Empty(legacy.Moves);
        AreaTrainerPokemonPayload member = new()
        {
            SpeciesId = _trainerSpecies, SpeciesName = _trainerSpeciesName, Slot = 2, Level = 35,
            AbilitiesRevealed = true, MovesRevealed = true,
            Abilities = [new() { Id = _trainerAbility, Name = _trainerAbilityName, Description = string.Empty }],
            Moves = [new() { Id = _trainerMove, Name = _trainerMoveName, Type = _trainerMoveType, Category = MoveCategory.Special, Description = _trainerMoveDescription }]
        };
        AreaTrainerPokemonPayload restored = TrackerJson.DeserializePayload<AreaTrainerPokemonPayload>(TrackerJson.SerializePayload(member));
        Assert.True(restored.AbilitiesRevealed);
        Assert.True(restored.MovesRevealed);
        Assert.Equal(_trainerAbility, Assert.Single(restored.Abilities).Id);
        AreaTrainerMovePayload move = Assert.Single(restored.Moves);
        Assert.Equal(MoveCategory.Special, move.Category);
        Assert.Equal(_trainerMoveDescription, move.Description);
    }
    private const string _preparationAreaId = "area:5";
    private const string _preparationAreaName = "Route 101";
    private const string _crossEnvironment = "cross";
    private const string _legacyAreaJson = """{"area_id":"area:5","name":"Route 101","encounter_total":100}""";
    private const string _itemId = "LEAFSTONE";
    private const string _itemName = "Leaf Stone";
    private const string _itemCategory = "evolution";
    private const string _hiddenFusionJson = """
        {"entry_id":"encounter_fusion:10:standard_cross:0:Land:1:0:Water:2","origin":"standard_cross","environment":"grass","cross_environment":true,"details_revealed":false,"first_encounter_type":"Land","first_slot":1,"second_encounter_type":"Water","second_slot":2,"fusion_chance_percent":5,"minimum_level":4,"maximum_level":6}
        """;

    /// <summary>
    /// Verifies older peers cannot accidentally present a zero-valued split as complete metadata.
    /// </summary>
    [Fact]
    public void LegacySummaryDoesNotInventSplitTotals()
    {
        using JsonDocument document = JsonDocument.Parse(_legacyAreaJson);
        AreaSummaryPayload area = TrackerJson.DeserializePayload<AreaSummaryPayload>(document.RootElement);
        Assert.Equal(100, area.EncounterTotal);
        Assert.Null(area.EncounterSlotTotal);
        Assert.Null(area.EncounterFusionTotal);
    }

    /// <summary>
    /// Verifies split totals and disclosed item categories survive the wire representation.
    /// </summary>
    [Fact]
    public void SplitCountsAndItemCategoryRoundTrip()
    {
        AreaSummaryPayload area = new() { AreaId = _preparationAreaId, Name = _preparationAreaName, EncounterTotal = 100, EncounterSlotTotal = 18, EncounterFusionTotal = 82, EncounterSlotsEncountered = 2, EncounterFusionsEncountered = 1 };
        AreaSummaryPayload restored = TrackerJson.DeserializePayload<AreaSummaryPayload>(TrackerJson.SerializePayload(area));
        Assert.Equal(18, restored.EncounterSlotTotal);
        Assert.Equal(82, restored.EncounterFusionTotal);
        Assert.Equal(2, restored.EncounterSlotsEncountered);
        Assert.Equal(1, restored.EncounterFusionsEncountered);
        AreaItemIdentityPayload item = new() { ItemId = _itemId, ItemName = _itemName, Category = _itemCategory };
        Assert.Equal(_itemCategory, TrackerJson.DeserializePayload<AreaItemIdentityPayload>(TrackerJson.SerializePayload(item)).Category);
    }

    /// <summary>
    /// Verifies a metadata-only fusion row deserializes without requiring or fabricating identities.
    /// </summary>
    [Fact]
    public void HiddenFusionRetainsSourcesWithoutIdentity()
    {
        using JsonDocument document = JsonDocument.Parse(_hiddenFusionJson);
        AreaEncounterFusionEntryPayload fusion = TrackerJson.DeserializePayload<AreaEncounterFusionEntryPayload>(document.RootElement);
        Assert.False(fusion.DetailsRevealed);
        Assert.Null(fusion.SpeciesId);
        Assert.Null(fusion.SpeciesName);
        Assert.Null(fusion.SpritePath);
        Assert.True(fusion.CrossEnvironment);
        Assert.Equal(5, fusion.FusionChancePercent);
        Assert.Equal(2, fusion.SecondSlot);
    }

    /// <summary>
    /// Verifies an unfinished environment page survives serialization without claiming that its empty rows are final.
    /// </summary>
    [Fact]
    public void PendingEncounterPageRoundTrips()
    {
        AreaLookupDetailResponsePayload response = new()
        {
            AreaId = _preparationAreaId,
            Name = _preparationAreaName,
            Category = AreaContentCategory.Encounter,
            EncounterEnvironment = _crossEnvironment,
            TotalCount = 96,
            Pending = true,
            OverworldEncounters = false,
            RequiredFusionMaterials = [new FusionMaterialAssignmentPayload { BodyId = 25, HeadId = 4 }]
        };

        AreaLookupDetailResponsePayload result = TrackerJson.DeserializePayload<AreaLookupDetailResponsePayload>(TrackerJson.SerializePayload(response));

        Assert.True(result.Pending);
        Assert.False(result.OverworldEncounters);
        Assert.Equal(96, result.TotalCount);
        Assert.Empty(result.EncounterFusions);
        Assert.Equal(_crossEnvironment, result.EncounterEnvironment);
        Assert.Equal(25, Assert.Single(result.RequiredFusionMaterials).BodyId);
    }

    /// <summary>
    /// Verifies native page assignments preserve their requested orientation and exact numeric result.
    /// </summary>
    [Fact]
    public void NativeEncounterAssignmentsRoundTrip()
    {
        AreaLookupDetailRequestPayload request = new()
        {
            AreaId = _preparationAreaId,
            Category = AreaContentCategory.Encounter,
            EncounterEnvironment = _crossEnvironment,
            UseNativeFusionMapping = true,
            FusionResults = [new AreaFusionResultPayload { BodyId = 25, HeadId = 4, SpeciesNumber = 12345 }]
        };
        AreaLookupDetailRequestPayload result = TrackerJson.DeserializePayload<AreaLookupDetailRequestPayload>(TrackerJson.SerializePayload(request));
        Assert.True(result.UseNativeFusionMapping);
        AreaFusionResultPayload pair = Assert.Single(result.FusionResults);
        Assert.Equal(25, pair.BodyId);
        Assert.Equal(4, pair.HeadId);
        Assert.Equal(12345, pair.SpeciesNumber);
    }

    /// <summary>
    /// Verifies recovery and encounter-mode events retain either live option value.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActiveEncounterModeRoundTrips(bool overworld)
    {
        GameCurrentStatePayload state = new(true, null, null, 0, overworldEncounters: overworld);
        GameCurrentStatePayload result = TrackerJson.DeserializePayload<GameCurrentStatePayload>(TrackerJson.SerializePayload(state));
        Assert.Equal(overworld, result.OverworldEncounters);
    }

    /// <summary>
    /// Initializes area lookup payload tests.
    /// </summary>
    public AreaLookupPayloadTests()
    {
    }

    /// <summary>
    /// Verifies summary counts and category names use the canonical protocol shape.
    /// </summary>
    [Fact]
    public void AreaSummaryRoundTrips()
    {
        AreaLookupSummaryResponsePayload response = new()
        {
            SchemaVersion = 1,
            Revision = 8,
            Areas =
            [
                new AreaSummaryPayload
                {
                    AreaId = "area:10",
                    Name = "Route 102",
                    MapIds = [10],
                    TrainerTotal = 4,
                    TrainerDefeated = 2,
                    EncounterTotal = 28,
                    Encountered = 3,
                    ItemTotal = 1,
                    ItemsCollected = 1
                }
            ]
        };

        JsonElement json = TrackerJson.SerializePayload(response);
        AreaLookupSummaryResponsePayload result = TrackerJson.DeserializePayload<AreaLookupSummaryResponsePayload>(json);

        Assert.Equal(8, result.Revision);
        Assert.Equal(4, Assert.Single(result.Areas).TrainerTotal);
        Assert.Equal(28, Assert.Single(result.Areas).EncounterTotal);
    }

    /// <summary>
    /// Verifies an undiscovered encounter retains public odds and levels while omitting identity.
    /// </summary>
    [Fact]
    public void HiddenEncounterOmitsSpeciesIdentity()
    {
        AreaLookupDetailResponsePayload response = new()
        {
            AreaId = "area:10",
            Name = "Route 102",
            Category = AreaContentCategory.Encounter,
            Revision = 0,
            Offset = 10,
            Limit = TrackerProtocol.AreaLookupPageSize,
            TotalCount = 28,
            EncounterEnvironment = "grass",
            EncounterEnvironments = [new AreaEncounterEnvironmentPayload { Key = "grass" }],
            Encounters =
            [
                new AreaEncounterEntryPayload
                {
                    EntryId = "encounter:10:0:Land:1",
                    MapId = 10,
                    EncounterType = "Land",
                    Slot = 1,
                    ProbabilityPercent = 40,
                    MinimumLevel = 3,
                    MaximumLevel = 4,
                    DetailsRevealed = false
                }
            ]
        };

        JsonElement json = TrackerJson.SerializePayload(response);
        JsonElement encounter = json.GetProperty("encounters")[0];

        Assert.Equal("encounter", json.GetProperty("category").GetString());
        Assert.Equal(10, json.GetProperty("offset").GetInt32());
        Assert.Equal(10, json.GetProperty("limit").GetInt32());
        Assert.Equal(28, json.GetProperty("total_count").GetInt32());
        Assert.Equal("grass", json.GetProperty("encounter_environment").GetString());
        Assert.Equal("grass", json.GetProperty("encounter_environments")[0].GetProperty("key").GetString());
        Assert.Equal(40, encounter.GetProperty("probability_percent").GetDouble());
        Assert.Equal(3, encounter.GetProperty("minimum_level").GetInt32());
        Assert.False(encounter.TryGetProperty("species_id", out _));
        Assert.False(encounter.TryGetProperty("species_name", out _));
        Assert.False(encounter.TryGetProperty("sprite_path", out _));
    }

    /// <summary>
    /// Verifies a revealed encounter carries both its display name and local sprite path.
    /// </summary>
    [Fact]
    public void RevealedEncounterRoundTripsIdentityAndSprite()
    {
        AreaEncounterEntryPayload encounter = new()
        {
            EntryId = "encounter:10:0:Land:1",
            MapId = 10,
            EncounterType = "Land",
            Slot = 1,
            ProbabilityPercent = 40,
            MinimumLevel = 3,
            MaximumLevel = 4,
            Encountered = true,
            DetailsRevealed = true,
            SpeciesId = "BULBASAUR:0",
            SpeciesName = "Bulbasaur",
            SpritePath = "Graphics/Battlers/001.png"
        };

        JsonElement json = TrackerJson.SerializePayload(encounter);
        AreaEncounterEntryPayload result = TrackerJson.DeserializePayload<AreaEncounterEntryPayload>(json);

        Assert.Equal("Bulbasaur", result.SpeciesName);
        Assert.Equal("Graphics/Battlers/001.png", result.SpritePath);
    }

    /// <summary>
    /// Verifies a discovered derived fusion retains both source tables and its conditional rate.
    /// </summary>
    [Fact]
    public void DerivedEncounterFusionRoundTripsBothSources()
    {
        AreaEncounterFusionEntryPayload fusion = new()
        {
            EntryId = "encounter_fusion:10:overworld_cross:0:Land:1:0:Water:2",
            Origin = "overworld_cross",
            Environment = "grass",
            CrossEnvironment = true,
            DetailsRevealed = true,
            Encountered = true,
            FirstEncounterType = "Land",
            FirstSlot = 1,
            SecondEncounterType = "Water",
            SecondSlot = 2,
            FusionChancePercent = 36,
            MinimumLevel = 4,
            MaximumLevel = 6,
            SpeciesId = "B1H2:0",
            SpeciesName = "Bulbamander",
            SpritePath = "Graphics/CustomBattlers/indexed/1/1.2.png"
        };

        JsonElement json = TrackerJson.SerializePayload(fusion);
        AreaEncounterFusionEntryPayload result = TrackerJson.DeserializePayload<AreaEncounterFusionEntryPayload>(json);

        Assert.True(result.CrossEnvironment);
        Assert.True(result.DetailsRevealed);
        Assert.True(result.Encountered);
        Assert.Equal("Water", result.SecondEncounterType);
        Assert.Equal(36, result.FusionChancePercent);
        Assert.Equal("Bulbamander", result.SpeciesName);
    }

    /// <summary>
    /// Verifies a revealed trainer party slot carries its local sprite path.
    /// </summary>
    [Fact]
    public void RevealedTrainerPokemonRoundTripsSprite()
    {
        AreaTrainerPokemonPayload pokemon = new()
        {
            Slot = 1,
            SpeciesId = "BULBASAUR:0",
            SpeciesName = "Bulbasaur",
            Level = 7,
            SpritePath = "Graphics/Battlers/001.png"
        };

        JsonElement json = TrackerJson.SerializePayload(pokemon);
        AreaTrainerPokemonPayload result = TrackerJson.DeserializePayload<AreaTrainerPokemonPayload>(json);

        Assert.Equal(7, result.Level);
        Assert.Equal("Graphics/Battlers/001.png", result.SpritePath);
    }

    /// <summary>
    /// Verifies collected item payloads can disclose the actual received item.
    /// </summary>
    [Fact]
    public void CollectedItemRoundTripsActualIdentity()
    {
        AreaItemEntryPayload item = new()
        {
            EntryId = "item:10:19",
            MapId = 10,
            X = 13,
            Y = 25,
            Kind = "standard_ground_item",
            Collected = true,
            DetailsRevealed = true,
            Items = [new AreaItemIdentityPayload { ItemId = "SUPERPOTION", ItemName = "Super Potion" }]
        };

        JsonElement json = TrackerJson.SerializePayload(item);
        AreaItemEntryPayload result = TrackerJson.DeserializePayload<AreaItemEntryPayload>(json);

        Assert.True(result.Collected);
        Assert.Equal("SUPERPOTION", Assert.Single(result.Items).ItemId);
    }
}
