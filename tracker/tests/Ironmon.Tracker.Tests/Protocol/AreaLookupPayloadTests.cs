namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies public area lookup payload serialization and redaction shapes.
/// </summary>
public sealed class AreaLookupPayloadTests
{
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
