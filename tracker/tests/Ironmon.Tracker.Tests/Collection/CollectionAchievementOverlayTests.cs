using System.Text;
using Ironmon.Tracker.Collection;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Checks renderer authorization, fallback responses, and the separation from durable unlock acknowledgements.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const string _overlayReadyKey = "ready";

    /// <summary>
    /// Opens real artwork only for earned achievements and reports unavailable renderers without clearing pending unlocks.
    /// </summary>
    /// <param name="earn">Whether a real trainer victory earns an achievement first.</param>
    /// <param name="available">Whether the desktop renderer accepts the presentation.</param>
    /// <returns>A task completing after wire responses, game-time updates, stale stop and progress preservation are checked.</returns>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task DesktopOverlayRequiresEarnedArtworkAndRetainsPendingUnlocks(bool earn, bool available)
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        if (earn)
            await store.AcceptBatchAsync(Batch(_stream, AchievementTrainer(_rivalClass, 1)));

        var earned = await store.GetAchievementsAsync();
        var pending = await store.GetAchievementsAsync(true);
        Guid id = earned.FirstOrDefault()?.Id ?? new Ironmon.Tracker.Collection.Achievements.AchievementPack().Nodes.First().Id;
        TrackerDiagnosticsStore diagnostics = new();
        using TrackerRequestSession session = new(diagnostics);
        using MemoryStream bytes = new();
        await using TrackerMessageWriter writer = new(bytes, leaveOpen: true);
        session.Connect(writer);
        GameHandshakePayload game = new(_previewTestVersion, _previewTestVersion, true, false, directory.Path, null, null) { AchievementOverlayVersion = GameplayCollectionProtocol.AchievementOverlayVersion };
        TrackerRequestClient client = new(session, new(0, _previewTestVersion, false, TimeSpan.FromSeconds(2)), new(), new(new TrackerKnowledgeOptions(directory.Path)));
        OverlayRecorder overlay = new() { Available = available };
        client.AttachAchievementOverlay(overlay);
        string presentation = Guid.NewGuid().ToString();
        await client.HandleAchievementOverlayAsync(TrackerMessageFactory.CreateEvent(GameplayCollectionProtocol.AchievementOverlayStart, 1, new { PresentationId = presentation, AchievementId = id }), store, game, CancellationToken.None);
        TrackerMessage response = TrackerMessageCodec.Deserialize(Encoding.UTF8.GetString(bytes.ToArray()).Trim());
        Assert.Equal(GameplayCollectionProtocol.AchievementOverlayReady, response.Event);
        Assert.Equal(earn && available, response.Payload.GetProperty(_overlayReadyKey).GetBoolean());
        Assert.Equal(earn, overlay.Presentation is not null);
        if (earn && available)
        {
            await client.HandleAchievementOverlayAsync(TrackerMessageFactory.CreateEvent(GameplayCollectionProtocol.AchievementOverlayFrame, 2, new { PresentationId = presentation, Elapsed = 0.09 }), store, game, CancellationToken.None);
            Assert.Equal(0.09, overlay.Elapsed);
            await client.HandleAchievementOverlayAsync(TrackerMessageFactory.CreateEvent(GameplayCollectionProtocol.AchievementOverlayStop, 3, new { PresentationId = Guid.NewGuid().ToString() }), store, game, CancellationToken.None);
            Assert.NotNull(overlay.Presentation);
            await client.HandleAchievementOverlayAsync(TrackerMessageFactory.CreateEvent(GameplayCollectionProtocol.AchievementOverlayStop, 4, new { PresentationId = presentation }), store, game, CancellationToken.None);
            Assert.Null(overlay.Presentation);
        }

        Assert.Equal(earned, await store.GetAchievementsAsync());
        Assert.Equal(pending, await store.GetAchievementsAsync(true));
        client.Disconnect();
        Assert.True(overlay.ResetCalled);
        Assert.Empty(diagnostics.Entries);
    }

    /// <summary>
    /// Records renderer calls without creating a desktop window in the cross-platform protocol checks.
    /// </summary>
    private sealed class OverlayRecorder : IAchievementNotificationOverlay
    {
        /// <summary>
        /// Gets or initializes whether preparation is accepted.
        /// </summary>
        internal bool Available { get; init; } = true;

        /// <summary>
        /// Gets the current authorized presentation.
        /// </summary>
        internal AchievementOverlayPresentation? Presentation { get; private set; }

        /// <summary>
        /// Gets the last authoritative animation time.
        /// </summary>
        internal double Elapsed { get; private set; }

        /// <summary>
        /// Gets whether disconnect released the renderer.
        /// </summary>
        internal bool ResetCalled { get; private set; }

        /// <summary>
        /// Records preparation without unlocking anything.
        /// </summary>
        /// <param name="presentation">The authorized material.</param>
        /// <param name="cancellationToken">The preparation cancellation token.</param>
        /// <returns>The configured renderer state.</returns>
        public Task<AchievementOverlayState> PrepareAsync(AchievementOverlayPresentation presentation, CancellationToken cancellationToken)
        {
            Presentation = presentation;
            return Task.FromResult(new AchievementOverlayState(Available, true));
        }

        /// <summary>
        /// Records game time for a matching presentation.
        /// </summary>
        /// <param name="presentationId">The presentation identity.</param>
        /// <param name="elapsedSeconds">The authoritative time.</param>
        /// <param name="cancellationToken">The update cancellation token.</param>
        /// <returns>The configured renderer state.</returns>
        public Task<AchievementOverlayState> UpdateAsync(string presentationId, double elapsedSeconds, CancellationToken cancellationToken)
        {
            Elapsed = elapsedSeconds;
            return Task.FromResult(new AchievementOverlayState(Available && Presentation?.PresentationId == presentationId, true));
        }

        /// <summary>
        /// Clears only the matching presentation.
        /// </summary>
        /// <param name="presentationId">The identity to remove.</param>
        public void Stop(string presentationId)
        {
            if (Presentation?.PresentationId == presentationId)
                Presentation = null;
        }

        /// <summary>
        /// Records disconnection cleanup.
        /// </summary>
        public void Reset()
        {
            ResetCalled = true;
            Presentation = null;
        }

        /// <summary>
        /// Completes the fake renderer lifetime.
        /// </summary>
        /// <returns>An already completed cleanup.</returns>
        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }
}
