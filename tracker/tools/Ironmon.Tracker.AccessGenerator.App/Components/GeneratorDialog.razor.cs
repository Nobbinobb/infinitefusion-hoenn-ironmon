using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Ironmon.Tracker.AccessGenerator.App.Components;

/// <summary>
/// Provides the shared modal frame and native focus isolation for generator dialogs.
/// </summary>
public partial class GeneratorDialog
{
    private const string _openFunction = "generatorDialog.open";
    private const string _closeFunction = "generatorDialog.close";
    private const string _escapeKey = "Escape";
    private ElementReference _element;

    /// <summary>
    /// Gets or sets the browser dialog bridge.
    /// </summary>
    [Inject]
    public IJSRuntime JavaScript { get; set; } = null!;

    /// <summary>
    /// Gets or sets the accessible dialog heading.
    /// </summary>
    [Parameter]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the context above the heading.
    /// </summary>
    [Parameter]
    public string? Eyebrow { get; set; }

    /// <summary>
    /// Gets or sets the dialog body and actions.
    /// </summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Gets or sets the dismissal callback.
    /// </summary>
    [Parameter]
    public EventCallback Closed { get; set; }

    /// <summary>
    /// Opens the native modal after its markup is available.
    /// </summary>
    /// <param name="firstRender">Whether the dialog has just been mounted.</param>
    /// <returns>The browser initialization task.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            await JavaScript.InvokeVoidAsync(_openFunction, _element);
    }

    /// <summary>
    /// Restores focus to the opening control and notifies the owner.
    /// </summary>
    /// <returns>The dismissal task.</returns>
    private async Task CloseAsync()
    {
        await JavaScript.InvokeVoidAsync(_closeFunction, _element);
        await Closed.InvokeAsync();
    }

    /// <summary>
    /// Dismisses the modal using Escape.
    /// </summary>
    /// <param name="args">The key pressed inside the modal.</param>
    /// <returns>The dismissal task or a completed task for another key.</returns>
    private Task HandleKeyDown(KeyboardEventArgs args)
        => args.Key == _escapeKey ? CloseAsync() : Task.CompletedTask;
}
