namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies canonical serialization for run-specific Pokemon obtainability payloads.
/// </summary>
public sealed class PokemonObtainabilityPayloadTests
{
    /// <summary>
    /// Verifies that active Debug requests carry bounded work without a completed-run recipe.
    /// </summary>
    [Fact]
    public void DebugRequestRoundTripsWithoutCompletedRecipe()
    {
        DebugPokemonObtainabilityRequestPayload expected = new()
        {
            SpeciesId = "B1H4:0",
            SpeciesIds = ["BULBASAUR:0", "B1H4:0"],
            EvolutionEdgeKeys = ["BULBASAUR:0>IVYSAUR:0"],
            Foreground = true,
            FusionMappingBatch = new PlayerFusionMappingBatchPayload
            {
                JobId = "run:job",
                Offset = 12,
                PackedPairs = [1, 4, 580, 581]
            }
        };

        JsonElement payload = TrackerJson.SerializePayload(expected);
        DebugPokemonObtainabilityRequestPayload actual = TrackerJson.DeserializePayload<DebugPokemonObtainabilityRequestPayload>(payload);

        Assert.Equal("B1H4:0", actual.SpeciesId);
        Assert.Equal(["BULBASAUR:0", "B1H4:0"], actual.SpeciesIds);
        Assert.Equal(["BULBASAUR:0>IVYSAUR:0"], actual.EvolutionEdgeKeys);
        Assert.True(actual.Foreground);
        Assert.Equal(12, actual.FusionMappingBatch!.Offset);
        Assert.Equal([1, 4, 580, 581], actual.FusionMappingBatch.PackedPairs);
        Assert.DoesNotContain("first_material_id", payload.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("recipe", payload.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that a maximum foreground mapping batch remains safely below the transport message limit.
    /// </summary>
    [Fact]
    public void ForegroundMappingBatchUsesCompactBoundedJson()
    {
        int[] packedPairs = new int[8_192 * 4];
        for (int index = 0; index < packedPairs.Length; index++)
            packedPairs[index] = 349_999 - index % 576;

        DebugPokemonObtainabilityRequestPayload request = new()
        {
            EvolutionEdgeKeys = [.. Enumerable.Range(0, 160).Select(index => $"B{index + 1}H{index + 2}:0>B{index + 3}H{index + 4}:0")],
            Foreground = true,
            FusionMappingBatch = new PlayerFusionMappingBatchPayload
            {
                JobId = "run:maximum-foreground-batch",
                Offset = 0,
                PackedPairs = packedPairs
            }
        };

        string json = TrackerJson.SerializePayload(request).GetRawText();

        Assert.True(json.Length < 400_000, $"The compact foreground mapping payload required {json.Length} characters.");
        Assert.DoesNotContain("first_material_id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("first_result_id", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that progress, proof paths, and item quantities round-trip through the tracker protocol.
    /// </summary>
    [Fact]
    public void ResponseRoundTripsProgressAndWitness()
    {
        PokemonObtainabilityResponsePayload expected = new()
        {
            Phase = "player_fusions",
            Complete = false,
            ProcessedPairs = 800,
            TotalPairs = 2_080,
            ObtainableCount = 412,
            UnresolvedSourceCount = 3,
            UnresolvedResourceCount = 1,
            FusionMappingMode = "tracker_worker",
            TrackerMappingBatchesApplied = 7,
            RubyMappingPairsProcessed = 0,
            ObtainableSpeciesIds = ["BULBASAUR:0", "B1H4:0"],
            ObtainableEvolutionEdgeKeys = ["BULBASAUR:0>IVYSAUR:0"],
            FusionMappingWork = new PlayerFusionMappingWorkPayload
            {
                JobId = "run:job",
                Seed = 123,
                GeneratorVersion = 3,
                BaseStatSourceFingerprint = "stats",
                CustomFusionPoolVersion = 2,
                CustomFusionPoolSize = 174_348,
                CustomFusionPoolFingerprint = "pool",
                MaterialIds = [1, 4, 7],
                ProcessedPairs = 2,
                TotalPairs = 6
            },
            Target = new PokemonObtainabilitySnapshot
            {
                Status = PokemonObtainabilityStatus.Obtainable,
                Reason = "Evolution",
                Path = ["Catch Bulbasaur", "Evolve into Ivysaur: Level 16"],
                RequiredItems = new Dictionary<string, int> { ["MOONSTONE"] = 1 }
            }
        };

        JsonElement payload = TrackerJson.SerializePayload(expected);
        PokemonObtainabilityResponsePayload actual = TrackerJson.DeserializePayload<PokemonObtainabilityResponsePayload>(payload);
        string json = payload.GetRawText();

        Assert.Equal(PokemonObtainabilityStatus.Obtainable, actual.Target!.Status);
        Assert.Equal(800, actual.ProcessedPairs);
        Assert.Equal(1, actual.UnresolvedResourceCount);
        Assert.Equal("tracker_worker", actual.FusionMappingMode);
        Assert.Equal(7, actual.TrackerMappingBatchesApplied);
        Assert.Equal(0, actual.RubyMappingPairsProcessed);
        Assert.Equal(["BULBASAUR:0", "B1H4:0"], actual.ObtainableSpeciesIds);
        Assert.Equal(["BULBASAUR:0>IVYSAUR:0"], actual.ObtainableEvolutionEdgeKeys);
        Assert.Equal([1, 4, 7], actual.FusionMappingWork!.MaterialIds);
        Assert.Equal(1, actual.Target.RequiredItems["MOONSTONE"]);
        Assert.Contains("\"status\":\"obtainable\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the foreground-only boundary survives protocol serialization independently of full completion.
    /// </summary>
    [Fact]
    public void ResponseRoundTripsBackgroundCompletionBoundary()
    {
        PokemonObtainabilityResponsePayload expected = new()
        {
            Phase = "authored_sources",
            Complete = false,
            BackgroundComplete = true,
            ProcessedPairs = 60_726,
            TotalPairs = 60_726
        };

        JsonElement payload = TrackerJson.SerializePayload(expected);
        PokemonObtainabilityResponsePayload actual = TrackerJson.DeserializePayload<PokemonObtainabilityResponsePayload>(payload);

        Assert.False(actual.Complete);
        Assert.True(actual.BackgroundComplete);
        Assert.Contains("\"background_complete\":true", payload.GetRawText(), StringComparison.Ordinal);
    }
}
