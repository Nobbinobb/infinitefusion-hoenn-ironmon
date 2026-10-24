using System.Text;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies bounded artwork requests use the direct game path without generated lookup preparation.
/// </summary>
public sealed class PokemonSpriteRequestTests
{
    private const string _species = "B117H339:0";
    private const string _missingSpecies = "PIKACHU:0";
    private const string _path = "Graphics/CustomBattlers/local_sprites/IronmonTracker/custom-339-117-main.png";
    private const string _nativeResponse = "{\"sprites\":{\"B117H339:0\":\"Graphics/CustomBattlers/local_sprites/IronmonTracker/custom-339-117-main.png\",\"PIKACHU:0\":null}}";
    private const string _run = "artwork-run";
    private const string _version = "test";
    private const string _directory = "ironmon-artwork-request-tests";

    /// <summary>
    /// Preserves native dictionary identities and nullable artwork while sending only the requested species.
    /// </summary>
    /// <returns>The correlated request and response verification task.</returns>
    [Fact]
    public async Task ArtworkDoesNotRequireGeneratedRunPreparation()
    {
        string root = Path.Combine(Path.GetTempPath(), _directory, Guid.NewGuid().ToString());
        try
        {
            using TrackerRequestSession session = new(new());
            using MemoryStream stream = new();
            await using TrackerMessageWriter writer = new(stream, leaveOpen: true);
            session.Connect(writer);
            TrackerConnectionState state = new();
            state.Publish(TrackerConnectionStatus.Connected, new(_version, _version, true, false, root, _run, null));
            TrackerRequestClient client = new(session, new(0, _version, false, TimeSpan.FromSeconds(2)), state, new(new TrackerKnowledgeOptions(root)));
            using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(5));
            Task<PokemonSpritesResponsePayload> pending = client.GetPokemonSpritesAsync([_species, _missingSpecies], cancellation.Token);
            while (!Encoding.UTF8.GetString(stream.ToArray()).EndsWith(TrackerProtocol.MessageDelimiter))
                await Task.Delay(10, cancellation.Token);

            string json = Encoding.UTF8.GetString(stream.ToArray()).Split(TrackerProtocol.MessageDelimiter, StringSplitOptions.RemoveEmptyEntries).Single();
            TrackerMessage request = TrackerMessageCodec.Deserialize(json);
            Assert.Equal(TrackerCommands.PokemonSprites, request.Command);
            Assert.Equal(new[] { _species, _missingSpecies }, TrackerJson.DeserializePayload<PokemonSpritesRequestPayload>(request.Payload).SpeciesIds);
            using JsonDocument native = JsonDocument.Parse(_nativeResponse);
            Assert.True(session.TryComplete(TrackerMessageFactory.CreateResponse(request.RequestId!, native.RootElement, request.RunId)));
            PokemonSpritesResponsePayload response = await pending;
            Assert.Equal(_path, response.Sprites[_species]);
            Assert.Null(response.Sprites[_missingSpecies]);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetPokemonSpritesAsync([]));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetPokemonSpritesAsync([.. Enumerable.Repeat(_species, 9)]));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
