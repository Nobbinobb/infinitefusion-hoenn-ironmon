using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Collection.Achievements;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Pages;

/// <summary>
/// Owns one selected detail image and cancels obsolete work when its selection or lifetime changes.
/// </summary>
public partial class AchievementArtworkDialog
{
    private CancellationTokenSource? _request;
    private AchievementArtwork? _artwork;
    private Guid? _requestedId;
    private bool _loading = true;
    private bool _disposed;

    /// <summary>
    /// Gets the earned-only artwork service.
    /// </summary>
    [Inject]
    private GameplayCollectionStore Collection { get; set; } = null!;

    /// <summary>
    /// Gets or sets the already revealed achievement selected in the collection page.
    /// </summary>
    [Parameter, EditorRequired]
    public UnlockedAchievement Achievement { get; set; } = null!;

    /// <summary>
    /// Gets or sets the action closing the viewer and restoring the opening control's focus.
    /// </summary>
    [Parameter]
    public EventCallback Closed { get; set; }

    /// <summary>
    /// Loads new selections while avoiding repeat reads during the page's background refreshes.
    /// </summary>
    /// <returns>A task completing after the selected image has loaded or become unavailable.</returns>
    protected override Task OnParametersSetAsync()
        => _requestedId == Achievement.Id ? Task.CompletedTask : LoadAsync();

    /// <summary>
    /// Replaces the active read and applies a result only while its selection remains current.
    /// </summary>
    /// <returns>A task completing after this read has finished or been superseded.</returns>
    private async Task LoadAsync()
    {
        _request?.Cancel();
        _request?.Dispose();
        CancellationTokenSource request = new();
        _request = request;
        _requestedId = Achievement.Id;
        _artwork = null;
        _loading = true;
        try
        {
            AchievementArtwork? artwork = await Collection.GetAchievementArtworkAsync(Achievement.Id, request.Token);
            if (!_disposed && ReferenceEquals(_request, request) && !request.IsCancellationRequested)
                _artwork = artwork;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
        }
        finally
        {
            if (!_disposed && ReferenceEquals(_request, request))
                _loading = false;
        }
    }

    /// <summary>
    /// Cancels pending work and releases the displayed data URL without writing a cache or viewing history.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _request?.Cancel();
        _request?.Dispose();
        _request = null;
        _artwork = null;
        GC.SuppressFinalize(this);
    }
}
