using System.Net;
using System.Net.Sockets;

namespace Ironmon.Tracker.Connection.Transport;

/// <summary>
/// Accepts one persistent game connection on the tracker loopback endpoint.
/// </summary>
public sealed class TrackerConnectionService : IAsyncDisposable
{
    private const string ObtainabilityIncompleteErrorCode = "obtainability_incomplete";
    private static readonly TimeSpan ObtainabilityPrecalculationInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ObtainabilityPrecalculationStartupDelay = TimeSpan.FromSeconds(2);
    private readonly Lock _lifecycleSync = new();
    private readonly TrackerDiagnosticAccessNotifier? _diagnosticAccessNotifier;
    private readonly TrackerDiagnosticsStore _diagnostics;
    private readonly TrackerGameEventProcessor _events;
    private readonly TrackerConnectionOptions _options;
    private readonly TrackerRequestSession _requests;
    private readonly TrackerConnectionState _state;
    private CancellationTokenSource? _cancellation;
    private TcpListener? _listener;
    private Task? _runTask;
    private bool _disposed;

    /// <summary>
    /// Initializes the tracker connection service.
    /// </summary>
    /// <param name="options">The listener and handshake options.</param>
    /// <param name="diagnostics">The bounded connection and protocol diagnostic store.</param>
    /// <param name="state">The shared connection state.</param>
    /// <param name="runState">The shared live run state.</param>
    /// <param name="knowledge">The tracker-owned discovery and annotation store.</param>
    /// <param name="areaDiscoveries">The tracker-owned area discovery store.</param>
    /// <param name="completedRuns">The tracker-owned completed-run recipe archive.</param>
    /// <param name="diagnosticAccess">The current signed diagnostic access, when configured.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public TrackerConnectionService(TrackerConnectionOptions options, TrackerDiagnosticsStore diagnostics, TrackerConnectionState state, TrackerRunState runState, TrackerKnowledgeStore knowledge, AreaDiscoveryStore areaDiscoveries, CompletedRunArchive completedRuns, DiagnosticAccessService? diagnosticAccess = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(runState);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(areaDiscoveries);
        ArgumentNullException.ThrowIfNull(completedRuns);
        _options = options;
        _diagnostics = diagnostics;
        _requests = new TrackerRequestSession(diagnostics);
        Requests = new TrackerRequestClient(_requests, options, state, areaDiscoveries, diagnosticAccess);
        _state = state;
        _events = new TrackerGameEventProcessor(state, runState, knowledge, areaDiscoveries, completedRuns, _requests, Requests);
        if (diagnosticAccess is not null)
            _diagnosticAccessNotifier = new TrackerDiagnosticAccessNotifier(diagnosticAccess, diagnostics, state, _requests, Requests);
    }

    /// <summary>
    /// Gets the actual bound loopback port after the listener starts.
    /// </summary>
    public int BoundPort { get; private set; }

    /// <summary>
    /// Gets the validated game request client attached to this connection.
    /// </summary>
    public TrackerRequestClient Requests { get; }

    /// <summary>
    /// Gets whether both the tracker launch mode and connected game authorize debug access.
    /// </summary>
    public bool DebugAuthorized => Requests.DebugAuthorized;

    /// <summary>
    /// Starts listening for the game without blocking the calling thread.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown after the service is disposed.</exception>
    public void Start()
    {
        lock (_lifecycleSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runTask is not null)
                return;

            try
            {
                _cancellation = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, _options.Port);
                _listener.Start();
                BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _state.Publish(TrackerConnectionStatus.Waiting);
                _diagnostics.RecordLifecycle(TrackerDiagnosticConstants.Listening, $"{IPAddress.Loopback}:{BoundPort}");
                _runTask = RunAsync(_listener, _cancellation.Token);
            }
            catch (SocketException exception)
            {
                _cancellation?.Dispose();
                _cancellation = null;
                _listener = null;
                BoundPort = 0;
                _state.Publish(TrackerConnectionStatus.Error, lastError: exception.Message);
                _diagnostics.RecordError(exception);
            }
        }
    }

    /// <summary>
    /// Stops accepting and processing game connections.
    /// </summary>
    /// <returns>A task representing listener shutdown.</returns>
    public async ValueTask StopAsync()
    {
        Task? runTask;
        CancellationTokenSource? cancellation;
        lock (_lifecycleSync)
        {
            runTask = _runTask;
            cancellation = _cancellation;
            _runTask = null;
            _cancellation = null;
            cancellation?.Cancel();
            _listener?.Stop();
            _listener = null;
            BoundPort = 0;
        }

        if (runTask is null)
            return;

        await runTask.ConfigureAwait(false);
        cancellation?.Dispose();
        _state.Publish(TrackerConnectionStatus.Stopped);
        _diagnostics.RecordLifecycle(TrackerDiagnosticConstants.Stopped, "Tracker listener stopped.");
    }

    /// <summary>
    /// Stops the listener and releases lifecycle resources.
    /// </summary>
    /// <returns>A task representing asynchronous disposal.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        if (_diagnosticAccessNotifier is not null)
            await _diagnosticAccessNotifier.DisposeAsync().ConfigureAwait(false);

        await StopAsync().ConfigureAwait(false);
        _requests.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// Accepts and processes game clients until listener shutdown.
    /// </summary>
    /// <param name="listener">The active loopback listener.</param>
    /// <param name="cancellationToken">The token that stops the listener.</param>
    /// <returns>A task representing the listener loop.</returns>
    private async Task RunAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await HandleClientAsync(client, cancellationToken).ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested)
                {
                    _state.Publish(TrackerConnectionStatus.Waiting);
                    _diagnostics.RecordLifecycle(TrackerDiagnosticConstants.Disconnected, "Game connection closed.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is IOException or SocketException or TrackerProtocolException)
            {
                _state.Publish(TrackerConnectionStatus.Error, lastError: exception.Message);
                _diagnostics.RecordError(exception);
            }
            finally
            {
                client.Dispose();
            }
        }
    }

    /// <summary>
    /// Completes the handshake and processes one persistent game connection.
    /// </summary>
    /// <param name="client">The accepted loopback game client.</param>
    /// <param name="cancellationToken">The token that stops the connection.</param>
    /// <returns>A task representing the client connection.</returns>
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        client.NoDelay = true;
        _state.Publish(TrackerConnectionStatus.Handshaking);
        _diagnostics.RecordLifecycle(TrackerDiagnosticConstants.Handshaking, client.Client.RemoteEndPoint?.ToString() ?? "Loopback game client");
        NetworkStream stream = client.GetStream();
        using TrackerMessageReader reader = new(stream, leaveOpen: true);
        await using TrackerMessageWriter writer = new(stream, leaveOpen: true);

        TrackerMessage handshakeMessage = await ReadHandshakeAsync(reader, cancellationToken).ConfigureAwait(false);
        _diagnostics.RecordIncoming(handshakeMessage);
        GameHandshakePayload game = ValidateGameHandshake(handshakeMessage);
        _events.SelectRun(game.RunId);
        Requests.ObtainabilityProgress.Reset();
        IReadOnlyList<string> diagnosticCapabilities = Requests.GetNegotiatedDiagnosticCapabilities(game);
        TrackerHandshakePayload tracker = new(_options.TrackerVersion, _options.DebugRequested, _options.AutoSelectStarter, _options.MaximumStarterBaseStatTotal, _options.FavoriteSpeciesIds, diagnosticCapabilities);
        TrackerMessage trackerHandshake = TrackerMessageFactory.CreateEvent(TrackerEvents.TrackerConnected, TrackerProtocol.InitialEventSequence, tracker, game.RunId, game.BattleId);
        await writer.WriteAsync(trackerHandshake, cancellationToken).ConfigureAwait(false);
        _diagnostics.RecordOutgoing(trackerHandshake);

        string requestId = Guid.NewGuid().ToString(TrackerProtocol.RequestIdFormat);
        Dictionary<string, object?> emptyPayload = [];
        TrackerMessage request = TrackerMessageFactory.CreateRequest(requestId, TrackerCommands.CurrentState, emptyPayload, game.RunId, game.BattleId);
        await writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        _diagnostics.RecordOutgoing(request);
        _requests.Connect(writer);

        _state.Publish(TrackerConnectionStatus.Connected, game);
        _diagnostics.RecordLifecycle(TrackerDiagnosticConstants.Connected, $"Infinite Fusion {game.GameVersion} · Ironmon {game.IronmonVersion}");

        using CancellationTokenSource precalculationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task precalculation = RunObtainabilityPrecalculationAsync(precalculationCancellation.Token);
        try
        {
            await ReadMessagesAsync(reader, requestId, game, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            precalculationCancellation.Cancel();
            try
            {
                await precalculation.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (precalculationCancellation.IsCancellationRequested)
            {
            }

            Requests.ObtainabilityProgress.Reset();
            ClearRequestSession();
        }
    }

    /// <summary>
    /// Advances authorized active-run obtainability in small tracker-driven batches while the game remains connected.
    /// </summary>
    /// <param name="cancellationToken">The token that stops work for the current game connection.</param>
    /// <returns>A task representing the background polling loop.</returns>
    private async Task RunObtainabilityPrecalculationAsync(CancellationToken cancellationToken)
    {
        string? completedRunId = null;
        await Task.Delay(ObtainabilityPrecalculationStartupDelay, cancellationToken).ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested)
        {
            TrackerConnectionSnapshot snapshot = _state.Snapshot;
            string? runId = snapshot.CurrentState?.RunId ?? snapshot.Game?.RunId;
            bool runCompleted = snapshot.CurrentState?.CompletedRun is not null;
            bool precalculationEligible = IsActiveRunObtainabilityPrecalculationEligible(
                snapshot.CurrentState?.IronmonActive == true,
                snapshot.CurrentState?.ActiveRunPreparationReady == true,
                runCompleted,
                runId,
                completedRunId,
                Requests.ArchiveObtainabilityPrecalculationSelected);

            if (precalculationEligible)
            {
                try
                {
                    PokemonObtainabilityResponsePayload response = await Requests.AdvanceActiveRunPreparationAsync(foreground: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (IsObtainabilityPrecalculationFinished(response))
                        completedRunId = runId;
                }
                catch (Exception exception) when (IsObtainabilityPrecalculationTerminalFailure(exception))
                {
                    _diagnostics.RecordError(exception);
                    completedRunId = runId;
                }
                catch (Exception exception) when (exception is IOException or TimeoutException or TrackerProtocolException)
                {
                    _diagnostics.RecordError(exception);
                }
            }
            await Task.Delay(ObtainabilityPrecalculationInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Determines whether a response has exhausted every phase that background precalculation may advance.
    /// </summary>
    /// <param name="response">The current game-owned obtainability state.</param>
    /// <returns>True when background polling should stop for the run; otherwise false.</returns>
    internal static bool IsObtainabilityPrecalculationFinished(PokemonObtainabilityResponsePayload response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Complete || response.BackgroundComplete;
    }

    /// <summary>
    /// Determines whether the connected active run may receive automatic background preparation.
    /// </summary>
    /// <param name="ironmonActive">Whether the connected game reports an active Ironmon mode.</param>
    /// <param name="preparationReady">Whether the game has reached the safe background-preparation boundary.</param>
    /// <param name="runCompleted">Whether the current run has already completed.</param>
    /// <param name="runId">The connected run identifier.</param>
    /// <param name="preparedRunId">The run identifier already prepared by this loop.</param>
    /// <param name="archiveSelected">Whether an explicitly expanded archived run owns preparation.</param>
    /// <returns>True when active-run background preparation may advance; otherwise false.</returns>
    internal static bool IsActiveRunObtainabilityPrecalculationEligible(bool ironmonActive, bool preparationReady, bool runCompleted, string? runId, string? preparedRunId, bool archiveSelected)
    {
        return ironmonActive
            && preparationReady
            && !runCompleted
            && !string.IsNullOrWhiteSpace(runId)
            && runId != preparedRunId
            && !archiveSelected;

    }

    /// <summary>
    /// Determines whether a rejected obtainability request represents a terminal adapter failure for the run.
    /// </summary>
    /// <param name="exception">The rejected tracker request or deterministic native-worker failure.</param>
    /// <returns>True when polling the same run cannot make further progress.</returns>
    internal static bool IsObtainabilityPrecalculationTerminalFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is InvalidOperationException
            || exception is TrackerProtocolException protocolException && protocolException.ErrorCode == ObtainabilityIncompleteErrorCode;
    }

    /// <summary>
    /// Reads the first client message within the configured handshake timeout.
    /// </summary>
    /// <param name="reader">The persistent message reader.</param>
    /// <param name="cancellationToken">The token that stops the connection.</param>
    /// <returns>The non-null handshake message.</returns>
    /// <exception cref="TrackerProtocolException">Thrown when the client closes before its handshake.</exception>
    private async Task<TrackerMessage> ReadHandshakeAsync(TrackerMessageReader reader, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.HandshakeTimeout);
        try
        {
            TrackerMessage? message = await reader.ReadAsync(timeout.Token).ConfigureAwait(false);
            return message ?? throw new TrackerProtocolException("The game closed before sending its handshake.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TrackerProtocolException("The game handshake timed out.");
        }
    }

    /// <summary>
    /// Validates and deserializes the game handshake event.
    /// </summary>
    /// <param name="message">The first client message.</param>
    /// <returns>The validated game handshake.</returns>
    /// <exception cref="TrackerProtocolException">Thrown when the message is not a game handshake.</exception>
    private static GameHandshakePayload ValidateGameHandshake(TrackerMessage message)
    {
        if (message.Type != TrackerMessageType.Event || message.Event != TrackerEvents.GameConnected)
            throw new TrackerProtocolException("The first game message must be game_connected.");

        return TrackerJson.DeserializePayload<GameHandshakePayload>(message.Payload);
    }

    /// <summary>
    /// Reads messages until the game disconnects and applies connection-level responses.
    /// </summary>
    /// <param name="reader">The persistent message reader.</param>
    /// <param name="currentStateRequestId">The current-state correlation identifier.</param>
    /// <param name="game">The connected game's handshake.</param>
    /// <param name="cancellationToken">The token that stops the connection.</param>
    /// <returns>A task representing the persistent read loop.</returns>
    private async Task ReadMessagesAsync(TrackerMessageReader reader, string currentStateRequestId, GameHandshakePayload game, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TrackerMessage? message = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (message is null)
                return;

            _diagnostics.RecordIncoming(message);
            if (message.Type == TrackerMessageType.Event)
            {
                await _events.ProcessAsync(message, game, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (message.Type == TrackerMessageType.Response && message.RequestId != currentStateRequestId)
            {
                _requests.TryComplete(message);
                continue;
            }

            if (message.Type != TrackerMessageType.Response || message.RequestId != currentStateRequestId)
                continue;

            if (message.Success != true)
                throw new TrackerProtocolException(message.Error?.Message ?? "The current_state request failed.");

            GameCurrentStatePayload currentState = TrackerJson.DeserializePayload<GameCurrentStatePayload>(message.Payload);
            _events.RecoverCurrentState(game, currentState);
        }
    }

    /// <summary>
    /// Clears the active request writer and fails outstanding requests after disconnection.
    /// </summary>
    private void ClearRequestSession()
    {
        Requests.Disconnect();
    }
}
