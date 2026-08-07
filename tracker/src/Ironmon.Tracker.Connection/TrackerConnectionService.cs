using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Ironmon.Tracker.Protocol;

namespace Ironmon.Tracker.Connection;

/// <summary>
/// Accepts one persistent game connection on the tracker loopback endpoint.
/// </summary>
public sealed class TrackerConnectionService : IAsyncDisposable
{
    private readonly object _lifecycleSync = new();
    private readonly CompletedRunArchive _completedRuns;
    private readonly TrackerDiagnosticsStore _diagnostics;
    private readonly TrackerKnowledgeStore _knowledge;
    private readonly TrackerConnectionOptions _options;
    private readonly TrackerRunState _runState;
    private readonly TrackerConnectionState _state;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TrackerMessage>> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, FusionPreviewResponsePayload> _fusionPreviewCache = new();
    private readonly ConcurrentDictionary<string, PokemonLookupSnapshot> _pokemonLookupCache = new();
    private readonly ConcurrentDictionary<string, PokemonSearchResponsePayload> _pokemonSearchCache = new();
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly SemaphoreSlim _writerLock = new(1, 1);
    private TrackerMessageWriter? _activeWriter;
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
    /// <param name="completedRuns">The tracker-owned completed-run recipe archive.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public TrackerConnectionService(TrackerConnectionOptions options, TrackerDiagnosticsStore diagnostics, TrackerConnectionState state, TrackerRunState runState, TrackerKnowledgeStore knowledge, CompletedRunArchive completedRuns)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(runState);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(completedRuns);
        _options = options;
        _diagnostics = diagnostics;
        _state = state;
        _runState = runState;
        _knowledge = knowledge;
        _completedRuns = completedRuns;
    }

    /// <summary>
    /// Gets the actual bound loopback port after the listener starts.
    /// </summary>
    public int BoundPort { get; private set; }

    /// <summary>
    /// Gets whether both the tracker launch mode and connected game authorize debug access.
    /// </summary>
    public bool DebugAuthorized => _options.DebugRequested && _state.Snapshot.Game?.DebugAvailable == true;

    /// <summary>
    /// Searches the connected game for Pokemon names compatible with one completed-run recipe.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="query">The name fragment entered by the user.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="limit">The maximum number of matches to return.</param>
    /// <param name="normalOnly">Whether to restrict matches to normal species.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The matching stable Pokemon identifiers.</returns>
    public async Task<PokemonSearchResponsePayload> SearchPokemonAsync(CompletedRunRecipePayload recipe, string query, int offset = 0, int limit = 20, bool normalOnly = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 50);
        string normalizedQuery = query.Trim();
        string cacheKey = $"{recipe.RunId}|{normalOnly}|{offset}|{limit}|{normalizedQuery.ToUpperInvariant()}";
        if (_pokemonSearchCache.TryGetValue(cacheKey, out PokemonSearchResponsePayload? cached))
            return cached;

        PokemonSearchRequestPayload payload = new() { Query = normalizedQuery, Offset = offset, Limit = limit, NormalOnly = normalOnly, Recipe = recipe };
        PokemonSearchResponsePayload response = await SendRequestAsync<PokemonSearchRequestPayload, PokemonSearchResponsePayload>("pokemon_search", payload, recipe.RunId, cancellationToken);
        _pokemonSearchCache[cacheKey] = response;
        return response;
    }

    /// <summary>
    /// Requests complete deterministic information for one Pokemon in a completed run.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="speciesId">The selected stable species and form identifier.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The reconstructed Pokemon information.</returns>
    public async Task<PokemonLookupSnapshot> LookupPokemonAsync(CompletedRunRecipePayload recipe, string speciesId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        string cacheKey = $"{recipe.RunId}|{speciesId.ToUpperInvariant()}";
        if (_pokemonLookupCache.TryGetValue(cacheKey, out PokemonLookupSnapshot? cached))
            return cached;

        PokemonLookupRequestPayload payload = new()
        {
            SpeciesId = speciesId,
            Level = 100,
            Recipe = recipe
        };

        PokemonLookupSnapshot response = await SendRequestAsync<PokemonLookupRequestPayload, PokemonLookupSnapshot>("pokemon_lookup", payload, recipe.RunId, cancellationToken);
        _pokemonLookupCache[cacheKey] = response;
        return response;
    }

    /// <summary>
    /// Requests both deterministic Ironmon fusion orientations for two normal Pokemon.
    /// </summary>
    /// <param name="recipe">The completed-run reconstruction recipe.</param>
    /// <param name="firstSpeciesId">The first normal species identifier.</param>
    /// <param name="secondSpeciesId">The second normal species identifier.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The distinct deterministic fusion outcomes.</returns>
    public async Task<FusionPreviewResponsePayload> PreviewFusionAsync(CompletedRunRecipePayload recipe, string firstSpeciesId, string secondSpeciesId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstSpeciesId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secondSpeciesId);
        string cacheKey = $"{recipe.RunId}|{firstSpeciesId.ToUpperInvariant()}|{secondSpeciesId.ToUpperInvariant()}";
        if (_fusionPreviewCache.TryGetValue(cacheKey, out FusionPreviewResponsePayload? cached))
            return cached;

        FusionPreviewRequestPayload payload = new()
        {
            FirstSpeciesId = firstSpeciesId,
            SecondSpeciesId = secondSpeciesId,
            Recipe = recipe
        };

        FusionPreviewResponsePayload response = await SendRequestAsync<FusionPreviewRequestPayload, FusionPreviewResponsePayload>("fusion_preview", payload, recipe.RunId, cancellationToken);
        _fusionPreviewCache[cacheKey] = response;
        return response;
    }

    /// <summary>
    /// Requests the game-owned Ironmon inspector data for one current Pokemon.
    /// </summary>
    /// <param name="request">The Pokemon source selection.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The complete authorized inspector snapshot.</returns>
    public Task<DebugPokemonInspectorSnapshot> InspectPokemonAsync(DebugPokemonInspectionRequestPayload request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureDebugAuthorized();
        string? runId = GetConnectedRunId();
        return SendRequestAsync<DebugPokemonInspectionRequestPayload, DebugPokemonInspectorSnapshot>("debug_inspect_pokemon", request, runId, cancellationToken);
    }

    /// <summary>
    /// Requests game-owned run and randomizer diagnostics.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The authorized run diagnostics.</returns>
    public Task<DebugRunDiagnosticsSnapshot> GetDebugRunDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        EnsureDebugAuthorized();
        string? runId = GetConnectedRunId();
        DebugRunDiagnosticsRequestPayload request = new();
        return SendRequestAsync<DebugRunDiagnosticsRequestPayload, DebugRunDiagnosticsSnapshot>("debug_run_diagnostics", request, runId, cancellationToken);
    }

    /// <summary>
    /// Searches generated Pokemon in the active run through the authorized debug channel.
    /// </summary>
    /// <param name="query">The name fragment entered by the user.</param>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="limit">The maximum number of matches to return.</param>
    /// <param name="normalOnly">Whether to restrict matches to normal species.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The matching active-run Pokemon identifiers.</returns>
    public Task<PokemonSearchResponsePayload> SearchDebugPokemonAsync(string query, int offset = 0, int limit = 20, bool normalOnly = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 50);
        EnsureDebugAuthorized();
        DebugPokemonSearchRequestPayload request = new() { Query = query.Trim(), Offset = offset, Limit = limit, NormalOnly = normalOnly };
        return SendRequestAsync<DebugPokemonSearchRequestPayload, PokemonSearchResponsePayload>("debug_pokemon_search", request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Requests generated information for one Pokemon in the active debug run.
    /// </summary>
    /// <param name="speciesId">The selected stable species and form identifier.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The active-run generated Pokemon information.</returns>
    public Task<PokemonLookupSnapshot> LookupDebugPokemonAsync(string speciesId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        EnsureDebugAuthorized();
        DebugPokemonLookupRequestPayload request = new() { SpeciesId = speciesId };
        return SendRequestAsync<DebugPokemonLookupRequestPayload, PokemonLookupSnapshot>("debug_pokemon_lookup", request, GetConnectedRunId(), cancellationToken);
    }

    /// <summary>
    /// Requests both generated fusion orientations for the active debug run.
    /// </summary>
    /// <param name="firstSpeciesId">The first normal fusion material.</param>
    /// <param name="secondSpeciesId">The second normal fusion material.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The active-run fusion outcomes.</returns>
    public Task<FusionPreviewResponsePayload> PreviewDebugFusionAsync(string firstSpeciesId, string secondSpeciesId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstSpeciesId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secondSpeciesId);
        EnsureDebugAuthorized();
        DebugFusionPreviewRequestPayload request = new() { FirstSpeciesId = firstSpeciesId, SecondSpeciesId = secondSpeciesId };
        return SendRequestAsync<DebugFusionPreviewRequestPayload, FusionPreviewResponsePayload>("debug_fusion_preview", request, GetConnectedRunId(), cancellationToken);
    }

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
                _diagnostics.RecordLifecycle("Listening", $"127.0.0.1:{BoundPort}");
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
        _diagnostics.RecordLifecycle("Stopped", "Tracker listener stopped.");
    }

    /// <summary>
    /// Stops the listener and releases lifecycle resources.
    /// </summary>
    /// <returns>A task representing asynchronous disposal.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await StopAsync().ConfigureAwait(false);
        _requestLock.Dispose();
        _writerLock.Dispose();
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
                    _diagnostics.RecordLifecycle("Disconnected", "Game connection closed.");
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
        _diagnostics.RecordLifecycle("Handshaking", client.Client.RemoteEndPoint?.ToString() ?? "Loopback game client");
        NetworkStream stream = client.GetStream();
        using TrackerMessageReader reader = new(stream, leaveOpen: true);
        await using TrackerMessageWriter writer = new(stream, leaveOpen: true);

        TrackerMessage handshakeMessage = await ReadHandshakeAsync(reader, cancellationToken).ConfigureAwait(false);
        _diagnostics.RecordIncoming(handshakeMessage);
        GameHandshakePayload game = ValidateGameHandshake(handshakeMessage);
        _knowledge.SelectRun(game.RunId);
        TrackerHandshakePayload tracker = new(_options.TrackerVersion, _options.DebugRequested);
        TrackerMessage trackerHandshake = TrackerMessageFactory.CreateEvent("tracker_connected", 0, tracker, game.RunId, game.BattleId);
        await writer.WriteAsync(trackerHandshake, cancellationToken).ConfigureAwait(false);
        _diagnostics.RecordOutgoing(trackerHandshake);

        string requestId = Guid.NewGuid().ToString("N");
        Dictionary<string, object?> emptyPayload = [];
        TrackerMessage request = TrackerMessageFactory.CreateRequest(requestId, "current_state", emptyPayload, game.RunId, game.BattleId);
        await writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        _diagnostics.RecordOutgoing(request);
        lock (_lifecycleSync)
            _activeWriter = writer;

        _state.Publish(TrackerConnectionStatus.Connected, game);
        _diagnostics.RecordLifecycle("Connected", $"Infinite Fusion {game.GameVersion} · Ironmon {game.IronmonVersion}");

        try
        {
            await ReadMessagesAsync(reader, requestId, game, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ClearRequestSession();
        }
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
        if (message.Type != TrackerMessageType.Event || message.Event != "game_connected")
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
            if (message.Type == TrackerMessageType.Event && message.Event == "run_started")
            {
                GameCurrentStatePayload startedState = TrackerJson.DeserializePayload<GameCurrentStatePayload>(message.Payload);
                _knowledge.SelectRun(startedState.RunId);
                _state.Publish(TrackerConnectionStatus.Connected, game, startedState);
                _runState.Recover(startedState);
                ObserveRecoveredKnowledge(startedState);
                ObserveCompletedRun(startedState);
                continue;
            }

            if (message.Type == TrackerMessageType.Event && message.Event == "run_completed")
            {
                _completedRuns.Store(TrackerJson.DeserializePayload<CompletedRunRecipePayload>(message.Payload));
                continue;
            }

            if (message.Type == TrackerMessageType.Event)
            {
                ApplyGameEvent(message);
                continue;
            }

            if (message.Type == TrackerMessageType.Response && message.RequestId != currentStateRequestId)
            {
                if (message.RequestId is not null && _pendingRequests.TryRemove(message.RequestId, out TaskCompletionSource<TrackerMessage>? completion))
                    completion.TrySetResult(message);
                continue;
            }

            if (message.Type != TrackerMessageType.Response || message.RequestId != currentStateRequestId)
                continue;

            if (message.Success != true)
                throw new TrackerProtocolException(message.Error?.Message ?? "The current_state request failed.");

            GameCurrentStatePayload currentState = TrackerJson.DeserializePayload<GameCurrentStatePayload>(message.Payload);
            _knowledge.SelectRun(currentState.RunId);
            _state.Publish(TrackerConnectionStatus.Connected, game, currentState);
            _runState.Recover(currentState);
            ObserveRecoveredKnowledge(currentState);
            ObserveCompletedRun(currentState);
        }
    }

    /// <summary>
    /// Sends one correlated request over the active game connection.
    /// </summary>
    /// <typeparam name="TRequest">The request payload type.</typeparam>
    /// <typeparam name="TResponse">The successful response payload type.</typeparam>
    /// <param name="command">The game command.</param>
    /// <param name="payload">The request payload.</param>
    /// <param name="runId">The run identifier when the request is scoped to a run.</param>
    /// <param name="cancellationToken">The token that cancels the request.</param>
    /// <returns>The deserialized successful response.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no game is connected.</exception>
    /// <exception cref="TrackerProtocolException">Thrown when the game rejects the request.</exception>
    private async Task<TResponse> SendRequestAsync<TRequest, TResponse>(string command, TRequest payload, string? runId, CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TrackerMessageWriter writer;
            lock (_lifecycleSync)
                writer = _activeWriter ?? throw new InvalidOperationException("The game is not connected.");

            string requestId = Guid.NewGuid().ToString("N");
            TaskCompletionSource<TrackerMessage> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pendingRequests.TryAdd(requestId, completion))
                throw new InvalidOperationException("The tracker could not reserve a request identifier.");

            try
            {
                TrackerMessage request = TrackerMessageFactory.CreateRequest(requestId, command, payload, runId);
                await _writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
                    _diagnostics.RecordOutgoing(request);
                }
                finally
                {
                    _writerLock.Release();
                }

                TrackerMessage response = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                if (response.Success != true)
                    throw new TrackerProtocolException(response.Error?.Message ?? "The game rejected the tracker request.");

                return TrackerJson.DeserializePayload<TResponse>(response.Payload);
            }
            finally
            {
                _pendingRequests.TryRemove(requestId, out _);
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    /// <summary>
    /// Rejects debug requests unless both sides of the handshake authorized access.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when debug access is not authorized.</exception>
    private void EnsureDebugAuthorized()
    {
        if (!DebugAuthorized)
            throw new InvalidOperationException("Both the tracker launch mode and connected game must authorize debug access.");
    }

    /// <summary>
    /// Gets the active run identifier when reconnect recovery has provided one.
    /// </summary>
    /// <returns>The connected run identifier when available.</returns>
    private string? GetConnectedRunId()
        => _state.Snapshot.CurrentState?.RunId ?? _state.Snapshot.Game?.RunId;

    /// <summary>
    /// Clears the active request writer and fails outstanding requests after disconnection.
    /// </summary>
    private void ClearRequestSession()
    {
        lock (_lifecycleSync)
            _activeWriter = null;

        _fusionPreviewCache.Clear();
        _pokemonLookupCache.Clear();
        _pokemonSearchCache.Clear();

        foreach ((string requestId, TaskCompletionSource<TrackerMessage> completion) in _pendingRequests)
        {
            if (_pendingRequests.TryRemove(requestId, out _))
                completion.TrySetException(new IOException("The game disconnected before completing the tracker request."));
        }
    }

    /// <summary>
    /// Applies one supported live game event to the tracker run-state store.
    /// </summary>
    /// <param name="message">The validated game event.</param>
    private void ApplyGameEvent(TrackerMessage message)
    {
        _knowledge.SelectRun(message.RunId);
        Action apply = message.Event switch
        {
            "battle_started" => () => _runState.StartBattle(TrackerJson.DeserializePayload<BattleSnapshot>(message.Payload)),
            "battle_ended" => _runState.EndBattle,
            "player_sent_out" or "player_state_changed" => () => ApplyPlayerUpdate(message),
            "player_move_menu_opened" => () => _runState.OpenPlayerMoveMenu(TrackerJson.DeserializePayload<PlayerMoveMenuOpenedPayload>(message.Payload)),
            "enemy_sent_out" or "enemy_state_changed" => () => ApplyEnemyUpdate(message),
            "enemy_move_used" => () =>
                _knowledge.ObserveEnemyMove(TrackerJson.DeserializePayload<EnemyMoveUsedPayload>(message.Payload)),
            "enemy_ability_revealed" => () =>
                _knowledge.ObserveEnemyAbility(TrackerJson.DeserializePayload<EnemyAbilityRevealedPayload>(message.Payload)),
            _ => () => { }
        };

        apply();
    }

    /// <summary>
    /// Applies a complete player update and its legal move discoveries.
    /// </summary>
    /// <param name="message">The player update event.</param>
    private void ApplyPlayerUpdate(TrackerMessage message)
    {
        PlayerPokemonSnapshot player = TrackerJson.DeserializePayload<PlayerPokemonSnapshot>(message.Payload);
        _runState.UpdatePlayer(player);
        _knowledge.ObservePlayer(player);
    }

    /// <summary>
    /// Applies an enemy update and remembers its most recently observed move.
    /// </summary>
    /// <param name="message">The enemy state event.</param>
    private void ApplyEnemyUpdate(TrackerMessage message)
    {
        EnemyPokemonSnapshot enemy = TrackerJson.DeserializePayload<EnemyPokemonSnapshot>(message.Payload);
        _runState.UpdateEnemy(enemy);
        _knowledge.ObserveEnemy(enemy);
        ObserveEnemyMove(enemy);
    }

    /// <summary>
    /// Restores legally observable knowledge included in a complete game-state snapshot.
    /// </summary>
    /// <param name="state">The recovered game state.</param>
    private void ObserveRecoveredKnowledge(GameCurrentStatePayload state)
    {
        if (state.Player is not null)
            _knowledge.ObservePlayer(state.Player);

        foreach (EnemyPokemonSnapshot enemy in state.Enemies)
        {
            _knowledge.ObserveEnemy(enemy);
            ObserveEnemyMove(enemy);
        }
    }

    /// <summary>
    /// Persists the completed-run recipe included in recovered game state.
    /// </summary>
    /// <param name="state">The recovered game state.</param>
    private void ObserveCompletedRun(GameCurrentStatePayload state)
    {
        if (state.CompletedRun is not null)
            _completedRuns.Store(state.CompletedRun);
    }

    /// <summary>
    /// Remembers a move included in an enemy snapshot when one is present.
    /// </summary>
    /// <param name="enemy">The complete legal enemy snapshot.</param>
    private void ObserveEnemyMove(EnemyPokemonSnapshot enemy)
    {
        if (enemy.LastMove is null)
            return;

        EnemyMoveUsedPayload observation = new()
        {
            EnemyId = enemy.EnemyId,
            SpeciesId = enemy.SpeciesId,
            EnemyLevel = enemy.Level,
            Move = enemy.LastMove
        };

        _knowledge.ObserveEnemyMove(observation);
    }
}
