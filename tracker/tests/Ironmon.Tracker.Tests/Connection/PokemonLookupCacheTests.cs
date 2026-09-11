using System.Text;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies generated lookup reuse without crossing preparation, run, or access boundaries.
/// </summary>
public sealed class PokemonLookupCacheTests
{
    private const string _runId = "lookup-run";
    private const string _otherRunId = "other-lookup-run";
    private const string _speciesId = "CHARMANDER:0";
    private const string _speciesName = "Charmander";
    private const string _version = "test";
    private const string _storageDirectory = "ironmon-lookup-cache-tests";

    /// <summary>
    /// Verifies only completed preparation and final target proofs permit request-free revisits.
    /// </summary>
    /// <param name="prepared">Whether background preparation has finished.</param>
    /// <param name="calculating">Whether the returned target proof is temporary.</param>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task RevisitsReuseOnlyPreparedFinalLookups(bool prepared, bool calculating)
    {
        using TrackerRequestSession session = new(new());
        using MemoryStream stream = new();
        await using TrackerMessageWriter writer = new(stream, leaveOpen: true);
        session.Connect(writer);
        TrackerConnectionOptions options = new(0, _version, true, TimeSpan.FromSeconds(1));
        TrackerConnectionState state = new();
        state.Publish(TrackerConnectionStatus.Connected, CreateGame(_runId));
        TrackerRequestClient client = CreateClient(session, options, state);
        if (prepared)
            CompletePreparation(client, _runId);

        PokemonLookupSnapshot first = await ReplyAsync(client, session, stream, calculating);
        long length = stream.Length;
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(2));
        Task<PokemonLookupSnapshot> repeat = client.LookupDebugPokemonAsync(_speciesId, cancellationToken: cancellation.Token);
        if (prepared && !calculating)
        {
            Assert.Same(first, await repeat);
            Assert.Equal(length, stream.Length);
        }
        else
        {
            Assert.True(stream.Length > length);
            CompleteRequest(session, stream, calculating);
            Assert.NotSame(first, await repeat);
        }
    }

    /// <summary>
    /// Verifies a cached response cannot bypass revoked access, another run, or a reconnect.
    /// </summary>
    [Fact]
    public async Task CachedLookupRespectsAuthorizationRunAndDisconnect()
    {
        using TrackerRequestSession session = new(new());
        using MemoryStream stream = new();
        await using TrackerMessageWriter writer = new(stream, leaveOpen: true);
        session.Connect(writer);
        TrackerConnectionOptions options = new(0, _version, true, TimeSpan.FromSeconds(1));
        TrackerConnectionState state = new();
        state.Publish(TrackerConnectionStatus.Connected, CreateGame(_runId));
        TrackerRequestClient client = CreateClient(session, options, state);
        CompletePreparation(client, _runId);
        PokemonLookupSnapshot first = await ReplyAsync(client, session, stream, false);
        Assert.Same(first, await client.LookupDebugPokemonAsync(_speciesId));

        state.Publish(TrackerConnectionStatus.Connected, CreateGame(_runId, ironmonActive: false));
        Assert.NotSame(first, await ReplyAsync(client, session, stream, false));
        state.Publish(TrackerConnectionStatus.Connected, CreateGame(_runId, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.LookupDebugPokemonAsync(_speciesId));
        state.Publish(TrackerConnectionStatus.Connected, CreateGame(_otherRunId));
        CompletePreparation(client, _otherRunId);
        Assert.NotSame(first, await ReplyAsync(client, session, stream, false));

        client.Disconnect();
        session.Connect(writer);
        state.Publish(TrackerConnectionStatus.Connected, CreateGame(_runId));
        Assert.NotSame(first, await ReplyAsync(client, session, stream, false));
    }

    /// <summary>
    /// Creates an isolated request client without starting background connection work.
    /// </summary>
    /// <param name="session">The request session.</param>
    /// <param name="options">The launch authorization options.</param>
    /// <param name="state">The authoritative connection state.</param>
    /// <returns>The client under test.</returns>
    private static TrackerRequestClient CreateClient(TrackerRequestSession session, TrackerConnectionOptions options, TrackerConnectionState state)
    {
        string root = Path.Combine(Path.GetTempPath(), _storageDirectory, Guid.NewGuid().ToString());
        return new(session, options, state, new(new TrackerKnowledgeOptions(root)));
    }

    /// <summary>
    /// Creates the negotiated game identity for one run.
    /// </summary>
    /// <param name="runId">The active run identity.</param>
    /// <param name="debugAvailable">Whether the game authorizes development access.</param>
    /// <param name="ironmonActive">Whether the game still has an active Ironmon run.</param>
    /// <returns>The authorized game handshake.</returns>
    private static GameHandshakePayload CreateGame(string runId, bool debugAvailable = true, bool ironmonActive = true)
        => new(_version, _version, ironmonActive, debugAvailable, Path.GetTempPath(), runId, null);

    /// <summary>
    /// Marks the real client progress state as prepared for the specified run.
    /// </summary>
    /// <param name="client">The client under test.</param>
    /// <param name="runId">The prepared run.</param>
    private static void CompletePreparation(TrackerRequestClient client, string runId)
    {
        client.ObtainabilityProgress.Begin(runId, TrackerObtainabilityProgressScope.ActiveRun);
        client.ObtainabilityProgress.Report(runId, TrackerObtainabilityProgressScope.ActiveRun, new() { BackgroundComplete = true });
    }

    /// <summary>
    /// Completes one outgoing lookup through the production protocol and response correlation.
    /// </summary>
    /// <param name="client">The client under test.</param>
    /// <param name="session">The connected request session.</param>
    /// <param name="stream">The captured outgoing protocol stream.</param>
    /// <param name="calculating">Whether to return a temporary proof.</param>
    /// <returns>The deserialized response.</returns>
    private static async Task<PokemonLookupSnapshot> ReplyAsync(TrackerRequestClient client, TrackerRequestSession session, MemoryStream stream, bool calculating)
    {
        long length = stream.Length;
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(2));
        Task<PokemonLookupSnapshot> request = client.LookupDebugPokemonAsync(_speciesId, cancellationToken: cancellation.Token);
        Assert.True(stream.Length > length);
        CompleteRequest(session, stream, calculating);
        return await request;
    }

    /// <summary>
    /// Verifies deferred occurrence negotiation and supplies a correlated game response.
    /// </summary>
    /// <param name="session">The connected request session.</param>
    /// <param name="stream">The captured outgoing protocol stream.</param>
    /// <param name="calculating">Whether to return a temporary proof.</param>
    private static void CompleteRequest(TrackerRequestSession session, MemoryStream stream, bool calculating)
    {
        string json = Encoding.UTF8.GetString(stream.ToArray()).Split(TrackerProtocol.MessageDelimiter, StringSplitOptions.RemoveEmptyEntries)[^1];
        TrackerMessage request = TrackerMessageCodec.Deserialize(json);
        Assert.True(TrackerJson.DeserializePayload<DebugPokemonLookupRequestPayload>(request.Payload).DeferOccurrences);
        PokemonLookupSnapshot response = new()
        {
            Identity = new()
            {
                SpeciesId = _speciesId, SpeciesName = _speciesName,
                Obtainability = new() { Status = calculating ? PokemonObtainabilityStatus.Calculating : PokemonObtainabilityStatus.Obtainable }
            },
            Overview = new() { TrainerOccurrences = new() { Pending = true } }
        };

        Assert.True(session.TryComplete(TrackerMessageFactory.CreateResponse(request.RequestId!, response, request.RunId)));
    }
}
