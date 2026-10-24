using System.Diagnostics;
using System.Text.Json;
using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Collection.Achievements;

namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Authorizes desktop notifications exclusively from authenticated earned achievements.
/// </summary>
public sealed partial class TrackerRequestClient
{
    private const string _overlayIdKey = "presentation_id";
    private const string _overlayAchievementKey = "achievement_id";
    private const string _overlayElapsedKey = "elapsed";
    private const string _overlayPngPrefix = "data:image/png;base64,";
    private IAchievementNotificationOverlay? _achievementOverlay;
    private string? _overlayPresentationId;
    private double _overlayElapsed;
    private long _overlayLastAcknowledged;
    private AchievementOverlayState? _overlayLastState;

    /// <summary>
    /// Gets whether this tracker has an installed desktop renderer.
    /// </summary>
    internal bool HasAchievementOverlay => _achievementOverlay is not null;

    /// <summary>
    /// Attaches the application-owned renderer before the tracker listener starts.
    /// </summary>
    /// <param name="overlay">The tracker-owned desktop renderer.</param>
    public void AttachAchievementOverlay(IAchievementNotificationOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _achievementOverlay = overlay;
    }

    /// <summary>
    /// Routes bounded game-clocked presentation events without acknowledging an unlock.
    /// </summary>
    /// <param name="message">The game presentation event.</param>
    /// <param name="store">The earned collection store, or null when notifications are disabled.</param>
    /// <param name="game">The connected game installation and negotiated capabilities.</param>
    /// <param name="cancellationToken">Cancels the current connection's work.</param>
    /// <returns>A task completing after presentation state or a fallback response is sent.</returns>
    internal async Task HandleAchievementOverlayAsync(TrackerMessage message, GameplayCollectionStore? store, GameHandshakePayload game, CancellationToken cancellationToken)
    {
        JsonElement payload = message.Payload;
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(_overlayIdKey, out JsonElement identity) || identity.ValueKind != JsonValueKind.String)
            return;

        string id = identity.GetString()!;
        if (id.Length > 64 || !Guid.TryParse(id, out _))
            return;

        if (message.Event == GameplayCollectionProtocol.AchievementOverlayStop)
        {
            _achievementOverlay?.Stop(id);
            if (_overlayPresentationId == id)
                _overlayPresentationId = null;

            return;
        }

        AchievementOverlayState state = new(false, false);
        try
        {
            if (_achievementOverlay is not null && store is not null && game.AchievementOverlayVersion == GameplayCollectionProtocol.AchievementOverlayVersion)
            {
                using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                budget.CancelAfter(TimeSpan.FromMilliseconds(500));
                if (message.Event == GameplayCollectionProtocol.AchievementOverlayStart)
                {
                    if (_overlayPresentationId == id)
                    {
                        state = await _achievementOverlay.UpdateAsync(id, _overlayElapsed, budget.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        AchievementOverlayPresentation? presentation = await ResolveOverlayAsync(id, payload, store, game.GameRoot, budget.Token).ConfigureAwait(false);
                        if (presentation is not null)
                        {
                            _overlayPresentationId = id;
                            _overlayElapsed = 0;
                            state = await _achievementOverlay.PrepareAsync(presentation, budget.Token).ConfigureAwait(false);
                        }
                    }
                }
                else if (_overlayPresentationId == id && payload.TryGetProperty(_overlayElapsedKey, out JsonElement elapsed) && elapsed.ValueKind == JsonValueKind.Number && elapsed.TryGetDouble(out double seconds) && double.IsFinite(seconds) && seconds >= 0 && seconds <= 3)
                {
                    _overlayElapsed = seconds;
                    state = await _achievementOverlay.UpdateAsync(id, seconds, budget.Token).ConfigureAwait(false);
                }
            }
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && error is not OutOfMemoryException)
        {
            _achievementOverlay?.Stop(id);
            if (_overlayPresentationId == id)
                _overlayPresentationId = null;
        }

        long now = Stopwatch.GetTimestamp();
        if (message.Event == GameplayCollectionProtocol.AchievementOverlayStart || state != _overlayLastState || Stopwatch.GetElapsedTime(_overlayLastAcknowledged, now).TotalSeconds >= 0.1)
        {
            await _session.SendEventAsync(GameplayCollectionProtocol.AchievementOverlayReady, new { PresentationId = id, Ready = state.Available, Visible = state.Visible }, null, cancellationToken).ConfigureAwait(false);
            _overlayLastAcknowledged = now;
            _overlayLastState = state;
        }
    }

    /// <summary>
    /// Resolves image access through the authenticated earned ledger.
    /// </summary>
    /// <param name="presentationId">The current opaque presentation identity.</param>
    /// <param name="payload">The stable earned achievement identity.</param>
    /// <param name="store">The earned collection service.</param>
    /// <param name="gameRoot">The negotiated game installation.</param>
    /// <param name="cancellationToken">Cancels image loading.</param>
    /// <returns>Authorized images, or null when the event has no legitimate image access.</returns>
    private async Task<AchievementOverlayPresentation?> ResolveOverlayAsync(string presentationId, JsonElement payload, GameplayCollectionStore store, string gameRoot, CancellationToken cancellationToken)
    {
        if (OptionalGuid(payload, _overlayAchievementKey) is not Guid id)
            return null;

        var earned = await store.GetAchievementsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        UnlockedAchievement? entry = earned.FirstOrDefault(item => item.Id == id);
        if (entry is null)
            return null;

        string name = entry.Name;
        var images = await store.GetAchievementIconsAsync([id], cancellationToken).ConfigureAwait(false);
        AchievementIcon? small = images.GetValueOrDefault(id);
        AchievementArtwork? detail = await store.GetAchievementArtworkAsync(id, cancellationToken).ConfigureAwait(false);

        return small is null ? null : new(presentationId, gameRoot, name, Convert.FromBase64String(small.ImageDataUri[_overlayPngPrefix.Length..]), detail is null ? null : Convert.FromBase64String(detail.ImageDataUri[_overlayPngPrefix.Length..]));
    }

    /// <summary>
    /// Reads an optional GUID without accepting other JSON value kinds.
    /// </summary>
    /// <param name="payload">The bounded event object.</param>
    /// <param name="key">The stable field name.</param>
    /// <returns>The parsed identity, or null.</returns>
    private static Guid? OptionalGuid(JsonElement payload, string key)
        => payload.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.TryGetGuid(out Guid id) ? id : null;

    /// <summary>
    /// Releases connection-owned renderer state.
    /// </summary>
    private void ResetAchievementOverlay()
    {
        _overlayPresentationId = null;
        _overlayLastState = null;
        _achievementOverlay?.Reset();
    }
}
