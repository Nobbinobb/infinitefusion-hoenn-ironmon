using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Offers permission-free calculation before the active-run world lookup becomes available.
/// </summary>
public partial class ActiveRunLookupPreparation : IDisposable
{
    private const string _descriptionKey = "Lookup.Live.PrepareDescription";
    private const string _backgroundKey = "Lookup.Live.PrepareBackground";
    private const string _calculatingKey = "Lookup.Live.PrepareCalculating";
    private const string _calculateKey = "Lookup.Live.PrepareButton";
    private const string _calculatingButtonKey = "Lookup.Live.PrepareBusyButton";
    private const string _retryKey = "Lookup.Live.PrepareRetry";
    private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(250);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _calculating;

    /// <summary>
    /// Gets or initializes the shared game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Connection { get; set; } = null!;

    /// <summary>
    /// Gets or sets preparation progress for the displayed active run.
    /// </summary>
    [Parameter]
    public TrackerObtainabilityProgressSnapshot Progress { get; set; } = TrackerObtainabilityProgressSnapshot.Idle;

    /// <summary>
    /// Prioritizes the existing full calculation until it finishes or this run's view is closed.
    /// </summary>
    /// <returns>The foreground preparation task.</returns>
    private async Task CalculateAsync()
    {
        if (_calculating)
            return;

        _calculating = true;
        CancellationToken cancellationToken = _lifetime.Token;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                PokemonObtainabilityResponsePayload response = await Connection.AdvanceActiveRunPreparationAsync(foreground: true, cancellationToken: cancellationToken);
                if (response.BackgroundComplete)
                    return;

                await Task.Delay(_pollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException or TrackerProtocolException)
        {
            // The shared progress state owns the error and the retry presentation.
        }
        finally
        {
            _calculating = false;
        }
    }

    /// <summary>
    /// Stops this view's priority requests without discarding the shared calculation.
    /// </summary>
    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
