using Ironmon.Tracker.Connection.Wardrobe;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Wardrobe;

/// <summary>
/// Displays only wardrobe sessions opened by the connected game, without an opening control.
/// </summary>
public partial class WardrobeDialogHost : IDisposable
{
    private TrackerWardrobeClient? _client;

    /// <summary>
    /// Gets the active game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Requests { get; set; } = null!;

    /// <summary>
    /// Subscribes to game-issued wardrobe lifecycle events.
    /// </summary>
    protected override void OnInitialized()
    {
        _client = Requests.Wardrobe;
        _client.Changed += HandleChanged;
    }

    /// <summary>
    /// Refreshes the host after a session or connection update.
    /// </summary>
    private void HandleChanged()
        => _ = InvokeAsync(StateHasChanged);

    /// <summary>
    /// Removes the game event subscription when the shell leaves the render tree.
    /// </summary>
    public void Dispose()
    {
        if (_client is not null)
            _client.Changed -= HandleChanged;

        GC.SuppressFinalize(this);
    }
}
