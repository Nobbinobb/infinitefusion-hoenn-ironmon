namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies optional passive obtainability on compact Pokemon lookup surfaces.
/// </summary>
public sealed class LookupObtainabilityStatusTests
{
    /// <summary>
    /// Verifies search matches preserve an available passive status.
    /// </summary>
    [Fact]
    public void PokemonSearchMatchPreservesObtainabilityStatus()
    {
        PokemonSearchResponsePayload expected = new()
        {
            Matches =
            [
                new PokemonSearchMatch
                {
                    SpeciesId = "B1H4:0",
                    SpeciesName = "Fusion",
                    Fusion = true,
                    ObtainabilityStatus = PokemonObtainabilityStatus.Obtainable
                }
            ],
            Total = 1
        };

        JsonElement payload = TrackerJson.SerializePayload(expected);
        PokemonSearchResponsePayload actual = TrackerJson.DeserializePayload<PokemonSearchResponsePayload>(payload);

        Assert.Equal(PokemonObtainabilityStatus.Obtainable, Assert.Single(actual.Matches).ObtainabilityStatus);
        Assert.Contains("\"obtainability_status\":\"obtainable\"", payload.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies relation, candidate, and target cards retain their independently supplied states.
    /// </summary>
    [Fact]
    public void PokemonCardsPreserveObtainabilityStatus()
    {
        PokemonRelationSnapshot relation = new()
        {
            SpeciesId = "BULBASAUR:0",
            SpeciesName = "Bulbasaur",
            Label = "Body material",
            ObtainabilityStatus = PokemonObtainabilityStatus.Unobtainable
        };

        EvolutionCandidateSnapshot candidate = new()
        {
            SpeciesId = "IVYSAUR:0",
            SpeciesName = "Ivysaur",
            ObtainabilityStatus = PokemonObtainabilityStatus.Calculating
        };

        EvolutionTargetSnapshot target = new()
        {
            SpeciesId = "VENUSAUR:0",
            SpeciesName = "Venusaur",
            ObtainabilityStatus = PokemonObtainabilityStatus.Obtainable
        };

        PokemonRelationSnapshot restoredRelation = TrackerJson.DeserializePayload<PokemonRelationSnapshot>(TrackerJson.SerializePayload(relation));
        EvolutionCandidateSnapshot restoredCandidate = TrackerJson.DeserializePayload<EvolutionCandidateSnapshot>(TrackerJson.SerializePayload(candidate));
        EvolutionTargetSnapshot restoredTarget = TrackerJson.DeserializePayload<EvolutionTargetSnapshot>(TrackerJson.SerializePayload(target));

        Assert.Equal(PokemonObtainabilityStatus.Unobtainable, restoredRelation.ObtainabilityStatus);
        Assert.Equal(PokemonObtainabilityStatus.Calculating, restoredCandidate.ObtainabilityStatus);
        Assert.Equal(PokemonObtainabilityStatus.Obtainable, restoredTarget.ObtainabilityStatus);
    }

    /// <summary>
    /// Verifies unauthorized or unavailable compact surfaces omit the optional field.
    /// </summary>
    [Fact]
    public void PokemonSearchMatchOmitsUnavailableObtainabilityStatus()
    {
        PokemonSearchMatch match = new()
        {
            SpeciesId = "BULBASAUR:0",
            SpeciesName = "Bulbasaur"
        };

        string json = TrackerJson.SerializePayload(match).GetRawText();

        Assert.DoesNotContain("obtainability_status", json, StringComparison.Ordinal);
    }
}
