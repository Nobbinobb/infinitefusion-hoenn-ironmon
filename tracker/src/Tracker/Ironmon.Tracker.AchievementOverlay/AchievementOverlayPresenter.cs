using Ironmon.Tracker.Connection.Transport;

namespace Ironmon.Tracker.AchievementOverlay;

/// <summary>
/// Renders game-clocked notifications on an owned native Windows thread inside the tracker process.
/// </summary>
public sealed class AchievementOverlayPresenter : IAchievementNotificationOverlay, IDisposable
{
    private const string _threadName = "Ironmon achievement overlay";
    private readonly Lock _startSync = new();
    private readonly TaskCompletionSource<OverlayDispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<string, nint> _findGame;
    private readonly Func<nint, bool> _isForeground;
    private Thread? _thread;
    private OverlayWindow? _window;
    private OverlayArtwork? _artwork;
    private string? _presentationId;
    private string? _pendingPresentationId;
    private nint _gameWindow;
    private double _elapsed;
    private long _generation;
    private int _disposed;

    /// <summary>
    /// Creates the presenter without starting a thread or opening a window.
    /// </summary>
    public AchievementOverlayPresenter() : this(NativeGameWindow.Find, NativeGameWindow.IsForegroundGame)
    {
    }

    /// <summary>
    /// Creates a renderer with test-owned discovery and visibility sources.
    /// </summary>
    /// <param name="findGame">Finds only the intended game window.</param>
    /// <param name="isForeground">Reads whether that game owns focus.</param>
    internal AchievementOverlayPresenter(Func<string, nint> findGame, Func<nint, bool> isForeground)
    {
        _findGame = findGame;
        _isForeground = isForeground;
    }

    /// <summary>
    /// Loads an authorized notification and creates its initially hidden native overlay.
    /// </summary>
    /// <param name="presentation">The authorized title, installation and PNG buffers.</param>
    /// <param name="cancellationToken">Cancels superseded or disconnected preparation.</param>
    /// <returns>The renderer availability and game foreground state.</returns>
    public async Task<AchievementOverlayState> PrepareAsync(AchievementOverlayPresentation presentation, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return new(false, false);

        long generation = Interlocked.Increment(ref _generation);
        Volatile.Write(ref _pendingPresentationId, presentation.PresentationId);
        OverlayDispatcher dispatcher = await GetDispatcherAsync(cancellationToken).ConfigureAwait(false);
        return await dispatcher.InvokeAsync(() =>
        {
            if (generation != Volatile.Read(ref _generation) || Volatile.Read(ref _disposed) != 0)
                return new AchievementOverlayState(false, false);

            Clear();
            try
            {
                _artwork = OverlayArtwork.FromPresentation(presentation);
                cancellationToken.ThrowIfCancellationRequested();
                if (generation != Volatile.Read(ref _generation) || Volatile.Read(ref _disposed) != 0)
                {
                    Clear();
                    return new AchievementOverlayState(false, false);
                }

                _gameWindow = _findGame(presentation.GameRoot);
                _window = new OverlayWindow(_gameWindow);
                _presentationId = presentation.PresentationId;
                _elapsed = 0;
                dispatcher.SetActive(true);
                return new AchievementOverlayState(true, _isForeground(_gameWindow));
            }
            catch
            {
                Clear();
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies only game-supplied animation time, including an unchanged time while paused.
    /// </summary>
    /// <param name="presentationId">The matching presentation identity.</param>
    /// <param name="elapsedSeconds">The authoritative visible animation time in seconds.</param>
    /// <param name="cancellationToken">Cancels the bounded update.</param>
    /// <returns>The renderer availability and current foreground visibility.</returns>
    public async Task<AchievementOverlayState> UpdateAsync(string presentationId, double elapsedSeconds, CancellationToken cancellationToken)
    {
        if (!_ready.Task.IsCompletedSuccessfully || Volatile.Read(ref _disposed) != 0)
            return new(false, false);

        return await _ready.Task.Result.InvokeAsync(() =>
        {
            if (_presentationId != presentationId || _window is null || !NativeGameWindow.IsWindow(_gameWindow))
                return new AchievementOverlayState(false, false);

            _elapsed = elapsedSeconds;
            RenderCurrent();
            return new AchievementOverlayState(_window is not null, _isForeground(_gameWindow));
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes only the named presentation without disturbing a newer one.
    /// </summary>
    /// <param name="presentationId">The presentation to remove.</param>
    public void Stop(string presentationId)
    {
        if (Volatile.Read(ref _pendingPresentationId) == presentationId)
            Interlocked.Increment(ref _generation);

        if (_ready.Task.IsCompletedSuccessfully && Volatile.Read(ref _disposed) == 0)
            _ready.Task.Result.Post(() => { if (_presentationId == presentationId) Clear(); });
    }

    /// <summary>
    /// Invalidates pending preparation and releases the current presentation on disconnect.
    /// </summary>
    public void Reset()
    {
        Interlocked.Increment(ref _generation);
        Volatile.Write(ref _pendingPresentationId, null);
        if (_ready.Task.IsCompletedSuccessfully && Volatile.Read(ref _disposed) == 0)
            _ready.Task.Result.Post(Clear);
    }

    /// <summary>
    /// Releases native resources and waits for the owned renderer thread to exit.
    /// </summary>
    /// <returns>A task completing after native cleanup and thread shutdown.</returns>
    public async ValueTask DisposeAsync()
    {
        bool started;
        lock (_startSync)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            started = _thread is not null;
        }

        Interlocked.Increment(ref _generation);
        if (!started)
            return;

        OverlayDispatcher dispatcher = await _ready.Task.ConfigureAwait(false);
        try
        {
            await dispatcher.InvokeAsync(() => { Clear(); return true; }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        finally
        {
            dispatcher.Shutdown();
        }

        await _finished.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
    }

    /// <summary>
    /// Supports synchronous container cleanup after normal application shutdown.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// Lazily starts the owned native queue when a notification needs it.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for startup.</param>
    /// <returns>The initialized native dispatcher.</returns>
    private Task<OverlayDispatcher> GetDispatcherAsync(CancellationToken cancellationToken)
    {
        lock (_startSync)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_thread is null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = _threadName };
                _thread.Start();
            }
        }

        return _ready.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Owns and pumps the native notification windows independently of MAUI's main UI thread.
    /// </summary>
    private void Run()
    {
        try
        {
            OverlayDispatcher dispatcher = new();
            _ready.TrySetResult(dispatcher);
            dispatcher.Run(HandleTick);
        }
        catch (Exception error)
        {
            _ready.TrySetException(error);
        }
        finally
        {
            Clear();
            _finished.TrySetResult();
        }
    }

    /// <summary>
    /// Tracks movement and visibility without advancing the game-owned clock.
    /// </summary>
    private void HandleTick()
    {
        try
        {
            RenderCurrent();
        }
        catch
        {
            Clear();
        }
    }

    /// <summary>
    /// Draws the current phase only while the intended game owns focus.
    /// </summary>
    private void RenderCurrent()
    {
        if (_window is null || _artwork is null)
            return;

        if (!NativeGameWindow.IsWindow(_gameWindow))
        {
            Clear();
            return;
        }

        if (!_isForeground(_gameWindow))
        {
            _window.Hide();
            return;
        }

        OverlayPlacement? placement = NativeGameWindow.Measure(_gameWindow);
        if (placement is not null)
            _window.ShowAligned(_artwork, placement, false, SlideFraction(_elapsed));
    }

    /// <summary>
    /// Calculates the approved quick entry, hold and exit phases from game time.
    /// </summary>
    /// <param name="elapsedSeconds">Elapsed visible notification time in seconds.</param>
    /// <returns>The visible slide fraction between zero and one.</returns>
    internal static double SlideFraction(double elapsedSeconds)
        => elapsedSeconds < 0.18 ? 1 - Math.Pow(1 - Math.Clamp(elapsedSeconds / 0.18, 0, 1), 3) : elapsedSeconds < 2.78 ? 1 : Math.Pow(1 - Math.Clamp((elapsedSeconds - 2.78) / 0.18, 0, 1), 3);

    /// <summary>
    /// Closes only the renderer-owned window and releases its decoded images.
    /// </summary>
    private void Clear()
    {
        if (_ready.Task.IsCompletedSuccessfully)
            _ready.Task.Result.SetActive(false);

        _window?.Dispose();
        _window = null;
        _artwork?.Dispose();
        _artwork = null;
        _presentationId = null;
        _gameWindow = 0;
    }
}
