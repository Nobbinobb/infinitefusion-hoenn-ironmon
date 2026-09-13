using System.Globalization;
using Ironmon.Updater.Infrastructure;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Ironmon.Tracker.App.Components.Updates;

/// <summary>
/// Presents tracker-owned update review over the unchanged originating tracker view.
/// </summary>
public partial class TrackerUpdatePanel : IAsyncDisposable
{
    private const string AttachDialog = "ironmonDialog.attach";
    private const string DetachDialog = "ironmonDialog.detach";
    private const string EscapeKey = "Escape";
    private const string NumberFormat = "N1";
    private const string DateFormat = "g";
    private const string CaptureScroll = "ironmonUpdateNavigation.capture";
    private const string RestoreScroll = "ironmonUpdateNavigation.restore";
    private ElementReference _root;
    private int? _dialogHandle;
    private bool _disposed;
    private bool _dialogTransition;

    /// <summary>
    /// Gets the shared signed update workflow and observable review state.
    /// </summary>
    [Inject]
    private TrackerUpdateSession Session { get; set; } = null!;

    /// <summary>
    /// Gets the existing accessible dialog focus and inert-background bridge.
    /// </summary>
    [Inject]
    private IJSRuntime JavaScript { get; set; } = null!;

    /// <summary>
    /// Gets the bounded navigation registry shared with mounted tracker surfaces.
    /// </summary>
    [Inject]
    private TrackerUpdateNavigation Navigation { get; set; } = null!;

    /// <summary>
    /// Gets whether the shared dialog shell contains the invitation or full update view.
    /// </summary>
    private bool DialogVisible => Session.IsOpen || Session.ShowStartupPrompt;

    /// <summary>
    /// Subscribes to progress without delaying the first normal tracker render.
    /// </summary>
    protected override void OnInitialized()
    {
        Session.Changed += HandleChanged;
        Navigation.Capture = CaptureNavigationAsync;
        Navigation.OpenUpdates = Session.OpenFromSettingsAsync;
    }

    /// <summary>
    /// Starts the quiet check after first render and maintains the focus trap only while details are open.
    /// </summary>
    /// <param name="firstRender">Whether the tracker has just rendered.</param>
    /// <returns>The dialog focus transition.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _ = Session.StartAsync();
            var scroll = Navigation.TakeScroll();
            if (scroll.ValueKind != System.Text.Json.JsonValueKind.Undefined)
                await JavaScript.InvokeVoidAsync(RestoreScroll, scroll);
        }

        if (_dialogTransition)
            return;

        _dialogTransition = true;
        try
        {
            if (DialogVisible && _dialogHandle is null)
                _dialogHandle = await JavaScript.InvokeAsync<int>(AttachDialog, _root);

            if ((_disposed || !DialogVisible) && _dialogHandle is { } handle)
            {
                _dialogHandle = null;
                await JavaScript.InvokeVoidAsync(DetachDialog, handle);
            }
        }
        finally
        {
            _dialogTransition = false;
            if (!_disposed && DialogVisible != (_dialogHandle is not null))
                StateHasChanged();
        }
    }

    /// <summary>
    /// Returns to the origin with Escape, or requests safe cancellation while work is still running.
    /// </summary>
    /// <param name="args">The focused dialog keyboard event.</param>
    private void HandleKeyDown(KeyboardEventArgs args)
    {
        if (args.Key != EscapeKey)
            return;

        if (Session.ShowStartupPrompt)
        {
            Session.DismissStartupPrompt();
        }
        else if (Session.IsBusy)
        {
            Session.Cancel();
        }
        else
        {
            Session.Close();
        }
    }

    /// <summary>
    /// Marshals worker progress onto the renderer without modifying the originating view.
    /// </summary>
    private void HandleChanged()
    {
        Navigation.IsUpdateOpen = DialogVisible;
        if (!_disposed)
            _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Captures component selections and scroll on their owning renderer immediately before handoff.
    /// </summary>
    /// <returns>The bounded opaque resume context.</returns>
    private async Task<string> CaptureNavigationAsync()
    {
        var result = string.Empty;
        await InvokeAsync(async () =>
        {
            var scroll = await JavaScript.InvokeAsync<System.Text.Json.JsonElement>(CaptureScroll);
            result = Navigation.Export(scroll);
        });
        return result;
    }

    /// <summary>
    /// Formats actual byte counts using the selected interface culture.
    /// </summary>
    /// <param name="bytes">The measured or signed number of bytes.</param>
    /// <returns>The localized number of mebibytes.</returns>
    private static string Megabytes(long bytes)
        => (bytes / (1024d * 1024d)).ToString(NumberFormat, CultureInfo.CurrentCulture);

    /// <summary>
    /// Restores background focusability when the dialog host leaves the renderer.
    /// </summary>
    /// <returns>The asynchronous focus cleanup.</returns>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        Session.Changed -= HandleChanged;
        Navigation.Capture = null;
        Navigation.OpenUpdates = null;
        Navigation.IsUpdateOpen = false;
        if (_dialogHandle is { } handle)
        {
            _dialogHandle = null;
            await JavaScript.InvokeVoidAsync(DetachDialog, handle);
        }
    }
}
