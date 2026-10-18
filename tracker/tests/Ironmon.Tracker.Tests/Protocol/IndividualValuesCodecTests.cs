namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies optional IV evidence remains compatible with older tracker peers.
/// </summary>
public sealed class IndividualValuesCodecTests
{
    private const string _olderStats = "{}";

    /// <summary>
    /// Preserves zero, perfect, effective and unknown IVs without manufacturing values for older payloads.
    /// </summary>
    [Fact]
    public void StatsRoundTripIndividualValuesAndKeepOlderPayloadsUnknown()
    {
        DebugPokemonStatsSnapshot stats = new() { Ivs = new() { Hp = 0, Attack = 31 }, EffectiveIvs = new() { Hp = 31, Attack = 31 } };
        DebugPokemonStatsSnapshot restored = TrackerJson.DeserializePayload<DebugPokemonStatsSnapshot>(TrackerJson.SerializePayload(stats));
        Assert.Equal(0, restored.Ivs!.Hp);
        Assert.Equal(31, restored.Ivs.Attack);
        Assert.Null(restored.Ivs.Speed);
        Assert.Equal(31, restored.EffectiveIvs!.Hp);
        DebugPokemonStatsSnapshot older = TrackerJson.DeserializePayload<DebugPokemonStatsSnapshot>(JsonSerializer.Deserialize<JsonElement>(_olderStats));
        Assert.Null(older.Ivs);
        Assert.Null(older.EffectiveIvs);
        PokemonLookupStatsSnapshot speciesOnly = TrackerJson.DeserializePayload<PokemonLookupStatsSnapshot>(JsonSerializer.Deserialize<JsonElement>(_olderStats));
        Assert.Null(speciesOnly.NumericSpeciesId);
    }
}
