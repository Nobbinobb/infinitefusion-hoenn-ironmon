using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Wardrobe;
using System.Buffers.Binary;

namespace Ironmon.Tracker.Connection.Wardrobe;

/// <summary>
/// Edits a wardrobe session issued by the game without providing an operation that opens it.
/// </summary>
public sealed class TrackerWardrobeClient
{
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _mutations = new(1, 1);
    private readonly TrackerRequestSession _requests;
    private string? _gameSessionId;

    /// <summary>
    /// Initializes wardrobe requests over the current game transport.
    /// </summary>
    /// <param name="requests">The correlated game request session.</param>
    internal TrackerWardrobeClient(TrackerRequestSession requests)
        => _requests = requests;

    /// <summary>
    /// Occurs after accepted game state or connection availability changes.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Gets the latest game-authoritative editing state, or null before any wardrobe was opened.
    /// </summary>
    public WardrobeSnapshotPayload? Snapshot
    {
        get { lock (_sync) return field; }
        private set;
    }

    /// <summary>
    /// Gets the catalog supplied by the currently open game session.
    /// </summary>
    public IReadOnlyList<WardrobeCatalogItemPayload> Catalog
    {
        get { lock (_sync) return field; }
        private set;
    } = [];

    /// <summary>
    /// Gets whether a matching game currently accepts wardrobe requests.
    /// </summary>
    public bool Connected
    {
        get { lock (_sync) return field; }
        private set;
    }

    /// <summary>
    /// Attaches a negotiated game connection while retaining a recoverable draft from that same game.
    /// </summary>
    /// <param name="game">The accepted game handshake.</param>
    internal void Connect(GameHandshakePayload game)
    {
        lock (_sync)
        {
            if (_gameSessionId != game.SessionId)
            {
                Snapshot = null;
                Catalog = [];
            }

            _gameSessionId = game.SessionId;
            Connected = game.WardrobeProtocolVersion == WardrobeProtocol.Version && !string.IsNullOrWhiteSpace(game.SessionId);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Disables requests without discarding the last acknowledged draft or permanent purchases.
    /// </summary>
    internal void Disconnect()
    {
        lock (_sync)
            Connected = false;

        Changed?.Invoke();
    }

    /// <summary>
    /// Observes a wardrobe event sent by the originating game process.
    /// </summary>
    /// <param name="snapshot">The complete or incremental game state.</param>
    /// <param name="game">The negotiated game connection.</param>
    internal void Observe(WardrobeSnapshotPayload snapshot, GameHandshakePayload game)
    {
        if (game.WardrobeProtocolVersion == WardrobeProtocol.Version)
            Publish(snapshot, game.SessionId, true);
    }

    /// <summary>
    /// Fetches existing session state after an uncertain response; this never opens a wardrobe.
    /// </summary>
    /// <param name="cancellationToken">The token cancelling the recovery request.</param>
    /// <returns>The game-authoritative current or most recently closed session.</returns>
    public async Task<WardrobeSnapshotPayload> RefreshAsync(CancellationToken cancellationToken = default)
    {
        string? game;
        lock (_sync)
        {
            if (!Connected)
                throw new InvalidOperationException("The game is disconnected.");

            game = _gameSessionId;
        }

        WardrobeSnapshotPayload result = await _requests.SendAsync<Dictionary<string, object>, WardrobeSnapshotPayload>(WardrobeProtocol.StateCommand, [], null, cancellationToken).ConfigureAwait(false);
        Publish(result, game, true);
        return result;
    }

    /// <summary>
    /// Sends one validated edit against the latest acknowledged revision and reconciles uncertain outcomes.
    /// </summary>
    /// <param name="action">The operation on an existing session.</param>
    /// <param name="appearance">The complete draft for an appearance change, or null.</param>
    /// <param name="pieceKey">The selected item for a purchase, or null.</param>
    /// <param name="profileId">The saved-outfit identity for profile operations, or null.</param>
    /// <param name="name">The requested saved-outfit name, or null.</param>
    /// <param name="cancellationToken">The token cancelling the request.</param>
    /// <returns>The acknowledged authoritative state.</returns>
    public async Task<WardrobeSnapshotPayload> ExecuteAsync(WardrobeAction action, WardrobeAppearancePayload? appearance = null, string? pieceKey = null, string? profileId = null, string? name = null, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(action) || action == WardrobeAction.None)
            throw new ArgumentOutOfRangeException(nameof(action));

        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WardrobeSnapshotPayload current = RequireOpen();
            WardrobeRequestPayload request = Identity(current) with
            {
                Action = action,
                OperationId = Guid.NewGuid().ToString(TrackerProtocol.RequestIdFormat),
                Appearance = appearance,
                PieceKey = pieceKey,
                ProfileId = profileId,
                Name = name
            };
            try
            {
                WardrobeSnapshotPayload result = await _requests.SendAsync<WardrobeRequestPayload, WardrobeSnapshotPayload>(WardrobeProtocol.ActionCommand, request, null, cancellationToken).ConfigureAwait(false);
                Publish(result, current.GameSessionId, false);
                return result;
            }
            catch (Exception exception) when (exception is TrackerProtocolException or TimeoutException)
            {
                try { await RefreshAsync(cancellationToken).ConfigureAwait(false); }
                catch (Exception recovery) when (recovery is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException or OperationCanceledException) { }
                throw;
            }
        }
        finally
        {
            _mutations.Release();
        }
    }

    /// <summary>
    /// Requests one bounded native image without changing the draft, ownership or equipped appearance.
    /// </summary>
    /// <param name="mode">The native viewing mode.</param>
    /// <param name="pieceKey">The candidate item for a thumbnail, or null for the assembled look.</param>
    /// <param name="accessorySlot">The native accessory slot when previewing an accessory.</param>
    /// <param name="headOnly">Whether a portrait thumbnail focuses on the hairstyle.</param>
    /// <param name="dyeField">The hue field for native swatches.</param>
    /// <param name="profileId">The saved outfit for a profile thumbnail, or null.</param>
    /// <param name="cancellationToken">The token cancelling obsolete image work.</param>
    /// <returns>The native PNG and any requested native color palette.</returns>
    public async Task<WardrobePreviewPayload> PreviewAsync(string mode, string? pieceKey = null, string? accessorySlot = null, bool headOnly = false, string dyeField = WardrobeProtocol.HairColor, string? profileId = null, CancellationToken cancellationToken = default)
    {
        WardrobeSnapshotPayload current = RequireOpen();
        WardrobeRequestPayload request = Identity(current) with { Mode = mode, PieceKey = pieceKey, AccessorySlot = accessorySlot, HeadOnly = headOnly, DyeField = dyeField, ProfileId = profileId };
        WardrobePreviewPayload result = await _requests.SendAsync<WardrobeRequestPayload, WardrobePreviewPayload>(WardrobeProtocol.PreviewCommand, request, null, cancellationToken).ConfigureAwait(false);
        if (result.WardrobeId != current.WardrobeId || result.Revision != current.Revision)
            throw new TrackerProtocolException("The wardrobe preview belongs to another appearance.");

        ValidatePreview(result);
        return result;
    }

    /// <summary>
    /// Returns the validated native PNG as an image source.
    /// </summary>
    /// <param name="preview">The bounded native preview.</param>
    /// <returns>A PNG data source suitable for the tracker WebView.</returns>
    public static string ImageSource(WardrobePreviewPayload preview)
    {
        ValidatePreview(preview);
        return WardrobeProtocol.PngDataPrefix + preview.PngBase64;
    }

    /// <summary>
    /// Rejects oversized, invalid or non-PNG native preview data.
    /// </summary>
    /// <param name="preview">The native preview response.</param>
    private static void ValidatePreview(WardrobePreviewPayload preview)
    {
        if (preview.Width is < 1 or > 512 || preview.Height is < 1 or > 512 || preview.PngBase64.Length > WardrobeProtocol.MaximumPngBytes * 4 / 3 + 4)
            throw new TrackerProtocolException("The wardrobe preview exceeds its image bounds.");

        byte[] bytes;
        try { bytes = Convert.FromBase64String(preview.PngBase64); }
        catch (FormatException exception) { throw new TrackerProtocolException("The wardrobe preview is invalid.", exception); }
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        ReadOnlySpan<byte> header = [73, 72, 68, 82];
        if (bytes.Length is < 33 or > WardrobeProtocol.MaximumPngBytes || !bytes.AsSpan().StartsWith(signature)
            || !bytes.AsSpan(12, 4).SequenceEqual(header)
            || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)) != preview.Width
            || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)) != preview.Height)
        {
            throw new TrackerProtocolException("The wardrobe preview is not a bounded PNG.");
        }
    }

    /// <summary>
    /// Gets an identity for the currently open, compatible game-owned session.
    /// </summary>
    /// <returns>The immutable authoritative session state.</returns>
    private WardrobeSnapshotPayload RequireOpen()
    {
        lock (_sync)
        {
            if (!Connected)
                throw new InvalidOperationException("The game is disconnected.");

            if (Snapshot is not { Open: true } current || current.GameSessionId != _gameSessionId)
                throw new InvalidOperationException("Open the wardrobe in the game first.");

            return current;
        }
    }

    /// <summary>
    /// Creates the exact game and revision identity used by a request.
    /// </summary>
    /// <param name="snapshot">The last accepted session state.</param>
    /// <returns>A request with no mutation selected.</returns>
    private static WardrobeRequestPayload Identity(WardrobeSnapshotPayload snapshot)
        => new() { GameSessionId = snapshot.GameSessionId, WardrobeId = snapshot.WardrobeId, Revision = snapshot.Revision, ProfileRevision = snapshot.ProfileRevision };

    /// <summary>
    /// Applies only state from the current game, rejecting stale responses and retaining an opening catalog.
    /// </summary>
    /// <param name="snapshot">The accepted native state.</param>
    /// <param name="gameSessionId">The transport's originating game identity.</param>
    /// <param name="allowNewSession">Whether this is a game event or explicit state recovery.</param>
    private void Publish(WardrobeSnapshotPayload snapshot, string? gameSessionId, bool allowNewSession)
    {
        lock (_sync)
        {
            if (!Connected || _gameSessionId != gameSessionId || snapshot.GameSessionId != gameSessionId)
                return;

            if (snapshot.ProtocolVersion != WardrobeProtocol.Version || snapshot.Revision < 0 || snapshot.ProfileRevision < 0 || snapshot.Points < 0)
                throw new TrackerProtocolException("The wardrobe state is invalid.");

            if (snapshot.Open && (string.IsNullOrWhiteSpace(snapshot.WardrobeId) || string.IsNullOrWhiteSpace(snapshot.Appearance.Clothes) || string.IsNullOrWhiteSpace(snapshot.Appearance.Hair)))
                throw new TrackerProtocolException("The wardrobe session is incomplete.");

            if (Snapshot is { } previous)
            {
                if (!allowNewSession && previous.WardrobeId != snapshot.WardrobeId)
                    return;

                if (previous.WardrobeId == snapshot.WardrobeId && (snapshot.Revision < previous.Revision || snapshot.ProfileRevision < previous.ProfileRevision))
                    return;

                if (previous.WardrobeId != snapshot.WardrobeId)
                    Catalog = [];
            }

            if (snapshot.Catalog is not null)
                Catalog = snapshot.Catalog;

            Snapshot = snapshot;
        }

        Changed?.Invoke();
    }
}
