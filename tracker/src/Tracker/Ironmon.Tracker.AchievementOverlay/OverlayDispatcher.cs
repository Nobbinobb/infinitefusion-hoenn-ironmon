using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Ironmon.Tracker.AchievementOverlay;

/// <summary>
/// Serializes renderer work on a plain Windows message queue, sleeping when no notification is active.
/// </summary>
internal sealed class OverlayDispatcher
{
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly uint _thread;
    private nuint _timer;
    private int _stopped;

    /// <summary>
    /// Initializes the queue on the renderer-owned thread before publishing its identity.
    /// </summary>
    internal OverlayDispatcher()
    {
        OverlayNative.InitializeThread();
        _thread = OverlayNative.GetCurrentThreadId();
    }

    /// <summary>
    /// Executes bounded renderer work, skipping operations canceled before execution.
    /// </summary>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="operation">The operation to perform on the native window's owner thread.</param>
    /// <param name="cancellationToken">Cancels queued work or waiting for completion.</param>
    /// <returns>The operation's result or its failure.</returns>
    internal Task<T> InvokeAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue(() =>
        {
            if (completion.Task.IsCompleted)
                return;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopped) != 0, this);
                completion.TrySetResult(operation());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        });
        if (Volatile.Read(ref _stopped) != 0)
            completion.TrySetException(new ObjectDisposedException(nameof(OverlayDispatcher)));
        else if (!OverlayNative.PostThreadMessage(_thread, OverlayNative.WorkMessage, 0, 0))
            completion.TrySetException(new Win32Exception(Marshal.GetLastPInvokeError()));

        return completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Queues idempotent cleanup without blocking the connection reader.
    /// </summary>
    /// <param name="operation">The renderer-owned cleanup.</param>
    internal void Post(Action operation)
    {
        _ = InvokeAsync(() => { operation(); return true; }, CancellationToken.None);
    }

    /// <summary>
    /// Starts or stops movement tracking without owning the notification's animation clock.
    /// </summary>
    /// <param name="active">Whether a notification currently needs alignment.</param>
    internal void SetActive(bool active)
    {
        if (active && _timer == 0)
        {
            _timer = OverlayNative.SetTimer(0, 0, 16, 0);
            if (_timer == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        else if (!active && _timer != 0)
        {
            OverlayNative.KillTimer(0, _timer);
            _timer = 0;
        }
    }

    /// <summary>
    /// Pumps only this renderer's messages until orderly shutdown is requested.
    /// </summary>
    /// <param name="tick">The bounded movement and visibility check.</param>
    internal void Run(Action tick)
    {
        try
        {
            int result;
            while ((result = OverlayNative.GetMessage(out OverlayNative.Message message, 0, 0, 0)) > 0)
            {
                if (message.Window == 0 && message.Id == OverlayNative.WorkMessage)
                {
                    Drain();
                }
                else if (message.Window == 0 && message.Id == OverlayNative.TimerMessage)
                {
                    if (_timer != 0 && message.WParam == _timer)
                        tick();
                }
                else
                {
                    OverlayNative.DispatchMessage(ref message);
                }
            }

            if (result < 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        finally
        {
            Volatile.Write(ref _stopped, 1);
            SetActive(false);
            Drain();
        }
    }

    /// <summary>
    /// Requests thread exit after previously awaited cleanup has completed.
    /// </summary>
    internal void Shutdown() => OverlayNative.Quit(_thread);

    /// <summary>
    /// Completes queued operations in order, including rejected work during shutdown.
    /// </summary>
    private void Drain()
    {
        while (_work.TryDequeue(out Action? operation))
            operation();
    }
}
