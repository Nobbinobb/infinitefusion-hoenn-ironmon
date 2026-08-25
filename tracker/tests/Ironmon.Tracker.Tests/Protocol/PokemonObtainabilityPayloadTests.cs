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
            FusionClosureResult = new PlayerFusionClosureResultPayload
            {
                JobId = "run:job",
                ObtainableFusionWords = [0U, 4U, uint.MaxValue],
                PackedExecutableEvolutionEdges = [1, 129, 1, 170, 1],
                ObtainableCount = 104_378
            }
        };

        JsonElement payload = TrackerJson.SerializePayload(expected);
        DebugPokemonObtainabilityRequestPayload actual = TrackerJson.DeserializePayload<DebugPokemonObtainabilityRequestPayload>(payload);

        Assert.Equal("B1H4:0", actual.SpeciesId);
        Assert.Equal(["BULBASAUR:0", "B1H4:0"], actual.SpeciesIds);
        Assert.Equal(["BULBASAUR:0>IVYSAUR:0"], actual.EvolutionEdgeKeys);
        Assert.True(actual.Foreground);
        Assert.Equal([0U, 4U, uint.MaxValue], actual.FusionClosureResult!.ObtainableFusionWords);
        Assert.Equal([1, 129, 1, 170, 1], actual.FusionClosureResult.PackedExecutableEvolutionEdges);
        Assert.Equal(104_378, actual.FusionClosureResult.ObtainableCount);
        Assert.DoesNotContain("first_material_id", payload.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("recipe", payload.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that a full native closure remains safely below the transport message limit.
    /// </summary>
    [Fact]
    public void ForegroundMappingResultUsesCompactBoundedJson()
    {
        DebugPokemonObtainabilityRequestPayload request = new()
        {
            EvolutionEdgeKeys = [.. Enumerable.Range(0, 160).Select(index => $"B{index + 1}H{index + 2}:0>B{index + 3}H{index + 4}:0")],
            Foreground = true,
            FusionClosureResult = new PlayerFusionClosureResultPayload
            {
                JobId = "run:maximum-foreground-result",
                ObtainableFusionWords = new uint[10_938],
                PackedExecutableEvolutionEdges = new byte[300_000],
                ObtainableCount = 104_378
            }
        };

        string json = TrackerJson.SerializePayload(request).GetRawText();

        Assert.True(json.Length < TrackerProtocol.MaximumMessageCharacters, $"The compact foreground mapping result required {json.Length} characters.");
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
            ObtainableSpeciesIds = ["BULBASAUR:0", "B1H4:0"],
            ObtainableEvolutionEdgeKeys = ["BULBASAUR:0>IVYSAUR:0"],
            FusionClosureWork = new PlayerFusionClosureWorkPayload
            {
                JobId = "run:job",
                SourceCatalogFingerprint = "source-catalog",
                Seed = 123,
                GeneratorVersion = 3,
                BaseStatSourceFingerprint = "stats",
                CustomFusionPoolVersion = 2,
                CustomFusionPoolSize = 174_348,
                CustomFusionPoolFingerprint = "pool",
                MaterialIds = [1, 4, 7],
                TotalPairs = 6,
                ExcludedPairOffsets = [4],
                FusionEvolutionGeneratorVersion = 4,
                FusionEvolutionRulesVersion = 2,
                EvolutionSourceFingerprint = "sources",
                EvolutionTaxonomyFingerprint = "taxonomy",
                EvolutionMethodFingerprint = "methods"
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
        Assert.Equal(["BULBASAUR:0", "B1H4:0"], actual.ObtainableSpeciesIds);
        Assert.Equal(["BULBASAUR:0>IVYSAUR:0"], actual.ObtainableEvolutionEdgeKeys);
        Assert.Equal([1, 4, 7], actual.FusionClosureWork!.MaterialIds);
        Assert.Equal([4], actual.FusionClosureWork.ExcludedPairOffsets);
        Assert.Equal(4, actual.FusionClosureWork.FusionEvolutionGeneratorVersion);
        Assert.Equal(2, actual.FusionClosureWork.FusionEvolutionRulesVersion);
        Assert.Equal("sources", actual.FusionClosureWork.EvolutionSourceFingerprint);
        Assert.Equal("taxonomy", actual.FusionClosureWork.EvolutionTaxonomyFingerprint);
        Assert.Equal("methods", actual.FusionClosureWork.EvolutionMethodFingerprint);
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
