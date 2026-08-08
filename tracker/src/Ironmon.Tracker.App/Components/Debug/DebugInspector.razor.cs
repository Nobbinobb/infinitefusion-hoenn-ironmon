using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Coordinates authorized Pokemon inspection and run diagnostics.
/// </summary>
public partial class DebugInspector
{
    private DebugInspectorPage _selectedPage = DebugInspectorPage.Overview;
    private DebugPokemonInspectorSnapshot? _pokemon;
    private DebugRunDiagnosticsSnapshot? _diagnostics;
    private string _selectedTarget = "player";
    private string? _error;
    private bool _loading;
    private bool _targetInitialized;

    /// <summary>
    /// Gets or initializes the active game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Connection { get; set; } = null!;

    /// <summary>
    /// Gets or sets the initialized player Pokemon when available.
    /// </summary>
    [Parameter]
    public PlayerPokemonSnapshot? Player { get; set; }

    /// <summary>
    /// Gets or sets the active enemy Pokemon choices.
    /// </summary>
    [Parameter]
    public IReadOnlyList<EnemyPokemonSnapshot> Enemies { get; set; } = [];

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Selects the initial available inspection target without overriding user selection.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (_targetInitialized)
            return;

        _selectedTarget = Player is not null
            ? "player"
            : Enemies.Count > 0
                ? $"enemy:{Enemies[0].Position}"
                : "player";

        _targetInitialized = true;
    }

    /// <summary>
    /// Loads the initial inspector snapshot after the component first renders.
    /// </summary>
    /// <param name="firstRender">Whether this is the first completed render.</param>
    /// <returns>A task representing the initial request.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        await InspectSelectedAsync();
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Stores and immediately inspects the newly selected debug target.
    /// </summary>
    /// <param name="args">The select element change.</param>
    /// <returns>A task representing the inspection request.</returns>
    private async Task SelectTarget(ChangeEventArgs args)
    {
        _selectedTarget = args.Value?.ToString() ?? "player";
        await InspectSelectedAsync();
    }

    /// <summary>
    /// Selects a debug page and loads diagnostics when required.
    /// </summary>
    /// <param name="page">The requested debug page.</param>
    /// <returns>A task representing any required request.</returns>
    private async Task SelectPageAsync(DebugInspectorPage page)
    {
        if (_loading)
            return;

        _selectedPage = page;
        _error = null;
        if (page == DebugInspectorPage.Diagnostics)
            await LoadDiagnosticsAsync();
    }

    /// <summary>
    /// Requests inspector data for the selected current Pokemon source.
    /// </summary>
    /// <returns>A task representing the request.</returns>
    private async Task InspectSelectedAsync()
    {
        if (_loading)
            return;

        _loading = true;
        _error = null;
        try
        {
            DebugPokemonInspectionRequestPayload request = CreateInspectionRequest();
            _pokemon = await Connection.InspectPokemonAsync(request);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            _pokemon = null;
            _error = exception.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Requests the current game-owned run diagnostics.
    /// </summary>
    /// <returns>A task representing the request.</returns>
    private async Task LoadDiagnosticsAsync()
    {
        _loading = true;
        _error = null;
        try
        {
            _diagnostics = await Connection.GetDebugRunDiagnosticsAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            _diagnostics = null;
            _error = exception.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Builds the protocol request represented by the current target selection.
    /// </summary>
    /// <returns>The current inspection request.</returns>
    private DebugPokemonInspectionRequestPayload CreateInspectionRequest()
    {
        string[] parts = _selectedTarget.Split(':', 2);
        return parts[0] switch
        {
            "player" => new DebugPokemonInspectionRequestPayload { Target = DebugPokemonTarget.Player },
            "enemy" => new DebugPokemonInspectionRequestPayload { Target = DebugPokemonTarget.Enemy, EnemyPosition = int.Parse(parts[1]) },
            _ => throw new InvalidOperationException("The selected inspection target is unavailable.")
        };
    }

    /// <summary>
    /// Gets the CSS class for one debug page button.
    /// </summary>
    /// <param name="page">The represented page.</param>
    /// <returns>The page button CSS classes.</returns>
    private string GetPageClass(DebugInspectorPage page)
        => page == _selectedPage ? "selected" : string.Empty;
}
