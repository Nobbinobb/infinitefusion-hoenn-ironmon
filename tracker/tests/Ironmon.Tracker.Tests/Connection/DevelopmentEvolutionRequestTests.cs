using System.Text;
using Ironmon.Tracker.Connection.Obtainability;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies development evolution loading reuses native assignments before requesting game-owned formatting.
/// </summary>
public sealed class DevelopmentEvolutionRequestTests
{
    private const string _runId = "development-evolution-run";
    private const string _pokemonId = "development-evolution-pokemon";
    private const string _fusionSpeciesId = "B117H339:0";
    private const string _normalSpeciesId = "PIKACHU:0";
    private const string _version = "test";
    private const string _entryId = "TACKLE";
    private const string _entryName = "Tackle";
    private const string _fixtureOutputVariable = "IRONMON_DEVELOPMENT_EVOLUTION_FIXTURE";
    private const string _fixtureRecipeVariable = "IRONMON_DEVELOPMENT_EVOLUTION_RECIPE";

    /// <summary>
    /// Keeps complete fusion scans outside the correlated game request and preserves requested shared catalogs.
    /// </summary>
    /// <param name="speciesId">The current normal or reported fusion species.</param>
    /// <returns>The complete native preparation and request verification task.</returns>
    [Theory]
    [InlineData(_fusionSpeciesId)]
    [InlineData(_normalSpeciesId)]
    public async Task DirectionsUsePreparedPokemonBoundAssignments(string speciesId)
    {
        using TrackerRequestSession session = new(new());
        using MemoryStream stream = new();
        await using TrackerMessageWriter writer = new(stream, leaveOpen: true);
        session.Connect(writer);
        TrackerConnectionState connection = new();
        connection.Publish(TrackerConnectionStatus.Connected, new(_version, _version, true, true, Path.GetTempPath(), _runId, null));
        TrackerDiagnosticAuthorizer authorizer = new(new(0, _version, true, TimeSpan.FromSeconds(1)), connection, null);
        PlayerFusionMappingCoordinator mappings = new();
        PlayerFusionMappingWorkerCatalog catalog = PlayerFusionMappingWorkerCatalog.Load();
        FusionAssignmentRecipePayload recipe = CreateRecipe(catalog);
        TrackerDiagnosticRequestClient client = new(session, authorizer, mappings);
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(90));
        Task<DebugDevelopmentStateSnapshot> pending = client.GetDevelopmentStateAsync(_runId, true, true, cancellation.Token);
        TrackerMessage first = await ReadRequestAsync(stream, null, cancellation.Token);
        DebugDevelopmentStateRequestPayload initial = TrackerJson.DeserializePayload<DebugDevelopmentStateRequestPayload>(first.Payload);
        Assert.True(initial.IncludeCatalogs);
        Assert.False(initial.IncludeEvolutions);
        Assert.True(initial.UseNativeEvolutionAssignments);
        Assert.Null(initial.PredecessorAssignments);
        DebugDevelopmentPlayerSnapshot player = new()
        {
            PokemonId = _pokemonId, SpeciesId = speciesId, SpeciesName = speciesId,
            Level = 5, AbilityName = string.Empty
        };
        DebugDevelopmentCatalogEntry entry = new() { Id = _entryId, Name = _entryName };
        Assert.True(session.TryComplete(TrackerMessageFactory.CreateResponse(first.RequestId!, new DebugDevelopmentStateSnapshot
        {
            Player = player, Abilities = [entry], Moves = [entry], Items = [entry],
            FusionAssignments = speciesId == _fusionSpeciesId ? recipe : null
        }, _runId)));
        TrackerMessage second = await ReadRequestAsync(stream, first.RequestId, cancellation.Token);
        DebugDevelopmentStateRequestPayload request = TrackerJson.DeserializePayload<DebugDevelopmentStateRequestPayload>(second.Payload);
        Assert.False(request.IncludeCatalogs);
        Assert.True(request.IncludeEvolutions);
        Assert.True(request.UseNativeEvolutionAssignments);
        Assert.Equal(player.PokemonId, request.PokemonId);
        Assert.Equal(player.SpeciesId, request.SpeciesId);
        Assert.Equal(JsonSerializer.Serialize(mappings.GetActiveEvolutionTargetAssignments(_runId, speciesId), TrackerJson.Options), JsonSerializer.Serialize(request.EvolutionAssignments, TrackerJson.Options));
        IReadOnlyList<EvolutionPredecessorAssignmentPayload>? predecessors = await mappings.GetActiveEvolutionPredecessorAssignmentsAsync(_runId, speciesId, cancellation.Token);
        Assert.Equal(JsonSerializer.Serialize(predecessors, TrackerJson.Options), JsonSerializer.Serialize(request.PredecessorAssignments, TrackerJson.Options));
        if (speciesId == _fusionSpeciesId)
        {
            Assert.NotNull(request.EvolutionAssignments);
            Assert.NotNull(request.PredecessorAssignments);
            string? fixturePath = Environment.GetEnvironmentVariable(_fixtureOutputVariable);
            if (!string.IsNullOrWhiteSpace(fixturePath))
                await File.WriteAllTextAsync(fixturePath, JsonSerializer.Serialize(new { recipe, request }, TrackerJson.Options), cancellation.Token);
        }

        Assert.True(session.TryComplete(TrackerMessageFactory.CreateResponse(second.RequestId!, new DebugDevelopmentStateSnapshot
        {
            Player = player, Evolutions = [entry], Devolutions = [entry]
        }, _runId)));
        DebugDevelopmentStateSnapshot result = await pending;
        Assert.Null(result.FusionAssignments);
        Assert.Single(result.Abilities);
        Assert.Single(result.Moves);
        Assert.Single(result.Items);
        Assert.Single(result.Evolutions);
        Assert.Single(result.Devolutions);
    }

    /// <summary>
    /// Creates a recipe compatible with the exact embedded release catalogs.
    /// </summary>
    /// <param name="catalog">The embedded native worker catalog.</param>
    /// <returns>The deterministic active-run recipe.</returns>
    private static FusionAssignmentRecipePayload CreateRecipe(PlayerFusionMappingWorkerCatalog catalog)
    {
        string? recipePath = Environment.GetEnvironmentVariable(_fixtureRecipeVariable);
        if (!string.IsNullOrWhiteSpace(recipePath))
            return JsonSerializer.Deserialize<FusionAssignmentRecipePayload>(File.ReadAllText(recipePath), TrackerJson.Options) ?? throw new InvalidDataException("The runtime evolution recipe is empty.");

        return new()
        {
            Seed = catalog.VerificationSeed,
            PlayerFusionGeneratorVersion = catalog.PlayerFusionGeneratorVersion,
            GeneratorVersion = catalog.FusionEvolutionGeneratorVersion,
            RulesVersion = catalog.FusionEvolutionRulesVersion,
            SourceFingerprint = catalog.EvolutionSourceFingerprint,
            TaxonomyFingerprint = catalog.EvolutionTaxonomyFingerprint,
            MethodFingerprint = catalog.EvolutionMethodFingerprint,
            BaseStatSourceFingerprint = catalog.BaseStatSourceFingerprint,
            TargetPoolVersion = catalog.CustomFusionPoolVersion,
            TargetPoolSize = catalog.CustomFusionPool.Count,
            TargetPoolFingerprint = catalog.CustomFusionPoolFingerprint
        };
    }

    /// <summary>
    /// Waits for a complete outgoing request after asynchronous native preparation.
    /// </summary>
    /// <param name="stream">The captured protocol writer output.</param>
    /// <param name="previousId">The earlier request to skip, if any.</param>
    /// <param name="cancellationToken">The token bounding request and worker completion.</param>
    /// <returns>The next complete correlated request.</returns>
    private static async Task<TrackerMessage> ReadRequestAsync(MemoryStream stream, string? previousId, CancellationToken cancellationToken)
    {
        while (true)
        {
            string text = Encoding.UTF8.GetString(stream.ToArray());
            if (text.EndsWith(TrackerProtocol.MessageDelimiter))
            {
                string json = text.Split(TrackerProtocol.MessageDelimiter, StringSplitOptions.RemoveEmptyEntries)[^1];
                TrackerMessage request = TrackerMessageCodec.Deserialize(json);
                if (request.RequestId != previousId)
                    return request;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}
