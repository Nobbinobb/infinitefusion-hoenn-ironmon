namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies diagnostic negotiation, legacy debug access, and request-specific capability policy.
/// </summary>
public sealed class TrackerDiagnosticAuthorizerTests
{
    /// <summary>
    /// Verifies that unrestricted legacy debug access requires approval from both processes.
    /// </summary>
    [Fact]
    public void DebugAuthorizationRequiresTrackerAndGameApproval()
    {
        TrackerConnectionOptions options = new(0, "test", true, TimeSpan.FromSeconds(1));
        TrackerConnectionState state = new();
        TrackerDiagnosticAuthorizer authorizer = new(options, state, null);

        Assert.False(authorizer.DebugAuthorized);
        state.Publish(TrackerConnectionStatus.Connected, CreateGame(debugAvailable: false));
        Assert.False(authorizer.DebugAuthorized);
        state.Publish(TrackerConnectionStatus.Connected, CreateGame(debugAvailable: true));
        Assert.True(authorizer.DebugAuthorized);
        Assert.True(authorizer.HasCapability("future.capability"));
    }

    /// <summary>
    /// Verifies that developer override is negotiated only through the legacy two-sided debug handshake.
    /// </summary>
    [Fact]
    public void DeveloperOverrideRequiresLegacyDebugNegotiation()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ironmon-authorizer-{Guid.NewGuid():N}");
        try
        {
            DiagnosticAccessTokenValidator validator = new(new DiagnosticAccessKeyring([]));
            using DiagnosticAccessService access = new(new TrackerKnowledgeOptions(root), validator, developerOverride: true);
            GameHandshakePayload game = CreateGame(debugAvailable: true, [DiagnosticCapabilities.RunSeed, "future.capability"]);
            TrackerConnectionState state = new();
            TrackerDiagnosticAuthorizer disabled = new(new TrackerConnectionOptions(0, "test", false, TimeSpan.FromSeconds(1)), state, access);
            TrackerDiagnosticAuthorizer enabled = new(new TrackerConnectionOptions(0, "test", true, TimeSpan.FromSeconds(1)), state, access);

            Assert.Empty(disabled.GetNegotiatedCapabilities(game));
            Assert.Equal([DiagnosticCapabilities.RunSeed], enabled.GetNegotiatedCapabilities(game));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies area and Pokémon policy mappings under unrestricted authorized access.
    /// </summary>
    [Fact]
    public void AuthorizedMappingsCoverSupportedAreaAndPokemonRequests()
    {
        TrackerConnectionOptions options = new(0, "test", true, TimeSpan.FromSeconds(1));
        TrackerConnectionState state = new();
        state.Publish(TrackerConnectionStatus.Connected, CreateGame(debugAvailable: true));
        TrackerDiagnosticAuthorizer authorizer = new(options, state, null);

        Assert.True(authorizer.HasAreaDetails(AreaContentCategory.Trainer));
        Assert.True(authorizer.HasAreaDetails(AreaContentCategory.Encounter));
        Assert.True(authorizer.HasAreaDetails(AreaContentCategory.Item));
        authorizer.EnsurePokemonSource(null);
        authorizer.EnsurePokemonSource(DebugPokemonTarget.Player);
        authorizer.EnsurePokemonSource(DebugPokemonTarget.Enemy);
        foreach (PokemonLookupSection section in Enum.GetValues<PokemonLookupSection>())
        {
            authorizer.EnsurePokemonLookup(section);
            authorizer.EnsurePokemonInformation(section);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => authorizer.HasAreaDetails((AreaContentCategory)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => authorizer.EnsurePokemonSource(DebugPokemonTarget.Party));
        Assert.Throws<ArgumentOutOfRangeException>(() => authorizer.EnsurePokemonLookup((PokemonLookupSection)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => authorizer.EnsurePokemonInformation((PokemonLookupSection)99));
    }

    /// <summary>
    /// Verifies that game support alone cannot authorize diagnostic requests without tracker access.
    /// </summary>
    [Fact]
    public void MissingTrackerAccessRejectsCapabilities()
    {
        TrackerConnectionOptions options = new(0, "test", false, TimeSpan.FromSeconds(1));
        TrackerConnectionState state = new();
        state.Publish(TrackerConnectionStatus.Connected, CreateGame(debugAvailable: false, [DiagnosticCapabilities.RunSeed]));
        TrackerDiagnosticAuthorizer authorizer = new(options, state, null);

        Assert.False(authorizer.HasCapability(DiagnosticCapabilities.RunSeed));
        Assert.Throws<InvalidOperationException>(() => authorizer.EnsureAll(DiagnosticCapabilities.RunSeed));
        Assert.Throws<InvalidOperationException>(() => authorizer.EnsureAny(DiagnosticCapabilities.RunSeed, DiagnosticCapabilities.RunConfiguration));
    }

    /// <summary>
    /// Creates a connected-game handshake for authorization tests.
    /// </summary>
    /// <param name="debugAvailable">Whether the game authorizes legacy development access.</param>
    /// <param name="capabilities">The diagnostic capabilities supported by the game.</param>
    /// <returns>The game handshake.</returns>
    private static GameHandshakePayload CreateGame(bool debugAvailable, IReadOnlyList<string>? capabilities = null)
        => new("test", "test", true, debugAvailable, @"C:\Game", "run", null, capabilities);
}
