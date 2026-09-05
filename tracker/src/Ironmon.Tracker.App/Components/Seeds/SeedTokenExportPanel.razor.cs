using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Ironmon.Tracker.App.Components.Seeds;

/// <summary>
/// Creates, copies, and saves a seeded-run token from a supplied reproduction recipe.
/// </summary>
public partial class SeedTokenExportPanel
{
    private string _token = string.Empty;
    private string? _message;
    private long _seed;
    private bool _succeeded;
    private bool _copied;
    private bool _busy;
    private bool _dialogOpen;
    private const string _clipboardFunction = "navigator.clipboard.writeText";
    private const string _tokenIdFormat = "N";

    /// <summary>
    /// Gets or sets whether export uses the shared redesigned section and token dialog.
    /// </summary>
    [Parameter]
    public bool Redesigned { get; set; }

    /// <summary>
    /// Gets or sets whether the redesigned export action appears as a compact archive button.
    /// </summary>
    [Parameter]
    public bool Compact { get; set; }

    /// <summary>
    /// Gets or sets a fixed archived reproduction recipe.
    /// </summary>
    [Parameter]
    public RunReproductionRecipePayload? Recipe { get; set; }

    /// <summary>
    /// Gets or sets the asynchronous active-run recipe provider.
    /// </summary>
    [Parameter]
    public Func<Task<RunReproductionRecipePayload>>? RecipeProvider { get; set; }

    /// <summary>
    /// Gets or sets the export panel title.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public required string Title { get; set; }

    /// <summary>
    /// Gets or sets the export panel description.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public required string Description { get; set; }

    /// <summary>
    /// Gets or sets whether export actions are unavailable.
    /// </summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>
    /// Gets or sets the callback reporting local export activity.
    /// </summary>
    [Parameter]
    public EventCallback<bool> BusyChanged { get; set; }

    /// <summary>
    /// Gets or initializes the seeded-run token codec.
    /// </summary>
    [Inject]
    private SeedTokenCodec Codec { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the application time source.
    /// </summary>
    [Inject]
    private TimeProvider TimeProvider { get; set; } = null!;

    /// <summary>
    /// Gets or initializes browser interoperability used for clipboard export.
    /// </summary>
    [Inject]
    private IJSRuntime JavaScript { get; set; } = null!;

    /// <summary>
    /// Gets or initializes native seeded-run file saving.
    /// </summary>
    [Inject]
    private SeedTokenFileSaver FileSaver { get; set; } = null!;

    /// <summary>
    /// Creates a new token from the fixed or asynchronously supplied recipe.
    /// </summary>
    private async Task CreateAsync()
    {
        if (Disabled || _busy)
            return;

        await BeginAsync();
        try
        {
            RunReproductionRecipePayload recipe = Recipe ?? await (RecipeProvider?.Invoke()
                ?? throw new InvalidOperationException("No seeded-run recipe is available."));
            _seed = recipe.Seed;
            _token = Codec.Create(recipe, Guid.NewGuid().ToString(_tokenIdFormat), TimeProvider.GetUtcNow());
            _succeeded = true;
            _message = Redesigned ? null : Text["Seeds.Messages.Exported"].Value;
            _dialogOpen = Redesigned;
        }
        catch (Exception exception) when (IsExportFailure(exception))
        {
            _message = Text["Seeds.Errors.ExportFailed"];
        }
        finally
        {
            await EndAsync();
        }
    }

    /// <summary>
    /// Copies the generated token and exposes persistent adjacent confirmation.
    /// </summary>
    private async Task CopyAsync()
    {
        if (Disabled || _busy)
            return;

        await BeginAsync();
        try
        {
            await JavaScript.InvokeVoidAsync(_clipboardFunction, _token);
            _copied = true;
            _succeeded = true;
        }
        catch (JSException)
        {
            _message = Text["Seeds.Errors.CopyFailed"];
        }
        finally
        {
            await EndAsync();
        }
    }

    /// <summary>
    /// Saves the generated token through the native file picker.
    /// </summary>
    private async Task SaveAsync()
    {
        if (Disabled || _busy)
            return;

        await BeginAsync();
        try
        {
            if (!await FileSaver.SaveAsync(_token, _seed))
                return;

            _succeeded = true;
            _message = Text["Seeds.Messages.Saved"];
        }
        catch (Exception exception) when (IsSaveFailure(exception))
        {
            _message = Text["Seeds.Errors.SaveFailed"];
        }
        finally
        {
            await EndAsync();
        }
    }

    /// <summary>
    /// Dismisses the exported token while retaining it for the current component lifetime.
    /// </summary>
    private void CloseDialog()
        => _dialogOpen = false;

    /// <summary>
    /// Starts one export operation and notifies the containing workflow.
    /// </summary>
    private async Task BeginAsync()
    {
        _busy = true;
        _copied = false;
        _message = null;
        _succeeded = false;
        await BusyChanged.InvokeAsync(true);
    }

    /// <summary>
    /// Completes one export operation and notifies the containing workflow.
    /// </summary>
    private async Task EndAsync()
    {
        _busy = false;
        await BusyChanged.InvokeAsync(false);
    }

    /// <summary>
    /// Gets whether an exception is safe to present as an export failure.
    /// </summary>
    /// <param name="exception">The caught exception.</param>
    /// <returns>Whether the exception belongs to an expected export boundary.</returns>
    private static bool IsExportFailure(Exception exception)
        => exception is IOException or InvalidOperationException or TrackerProtocolException or TimeoutException or ArgumentException;

    /// <summary>
    /// Gets whether an exception is safe to present as a file-save failure.
    /// </summary>
    /// <param name="exception">The caught exception.</param>
    /// <returns>Whether the exception belongs to an expected native save boundary.</returns>
    private static bool IsSaveFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException;
}
