using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Applies the shared redesign and keyboard focus boundary to a dialog, including legacy dialog content.
/// </summary>
public partial class ObsidianDialogScope : IAsyncDisposable
{
    private const string _attachFunction = "ironmonDialog.attach";
    private const string _detachFunction = "ironmonDialog.detach";
    private ElementReference _element;
    private int? _focusHandle;

    /// <summary>
    /// Gets the browser runtime used for modal focus and background isolation.
    /// </summary>
    [Inject]
    private IJSRuntime JavaScript { get; set; } = null!;

    /// <summary>
    /// Gets or sets the dialog content, which must contain a dialog landmark.
    /// </summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Gets or sets the dismissal callback.
    /// </summary>
    [Parameter]
    public EventCallback Closed { get; set; }

    /// <summary>
    /// Initializes focus after the dialog content exists.
    /// </summary>
    /// <param name="firstRender">Whether this is the first completed render of the dialog scope.</param>
    /// <returns>A task representing browser focus initialization.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            _focusHandle = await JavaScript.InvokeAsync<int>(_attachFunction, _element);
    }

    /// <summary>
    /// Dismisses the dialog with Escape without triggering page shortcuts.
    /// </summary>
    /// <param name="args">The keyboard event originating inside the dialog.</param>
    /// <returns>A task representing dismissal, or a completed task for another key.</returns>
    private Task HandleKeyDown(KeyboardEventArgs args)
        => args.Key == TrackerKeyboardKeys.Escape ? Closed.InvokeAsync() : Task.CompletedTask;

    /// <summary>
    /// Restores background interaction and the opening control's focus.
    /// </summary>
    /// <returns>A task representing focus restoration and modal cleanup.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_focusHandle is not null)
        {
            try
            {
                await JavaScript.InvokeVoidAsync(_detachFunction, _focusHandle.Value);
            }
            catch (JSDisconnectedException)
            {
            }
        }

        GC.SuppressFinalize(this);
    }
}
