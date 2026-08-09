using Microsoft.AspNetCore.Components;
using Ironmon.Tracker.App.Components.Lookup;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Coordinates combined current-Pokemon inspection, generated lookup, and run diagnostics.
/// </summary>
public partial class DebugInspector
{
    private DebugInspectorPage _selectedPage = DebugInspectorPage.Pokemon;
    private PokemonInformationPage _selectedPokemonPage = PokemonInformationPage.Overview;
    private DebugPokemonInspectorSnapshot? _pokemon;
    private PokemonLookupSnapshot? _lookup;
    private DebugRunDiagnosticsSnapshot? _diagnostics;
    private string _selectedTarget = DebugTargetIds.Player;
    private string? _requestedLookupSpeciesId;
    private string? _error;
    private bool _loading;
    private bool _backgroundRefresh;
    private bool _targetInitialized;
    private bool _refreshPending;
    private PlayerPokemonSnapshot? _observedPlayer;
    private IReadOnlyList<EnemyPokemonSnapshot> _observedEnemies = [];

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
    /// <returns>A task representing any inspection refresh required by new parameters.</returns>
    protected override async Task OnParametersSetAsync()
    {
        if (!_targetInitialized)
        {
            _selectedTarget = Player is not null
                ? DebugTargetIds.Player
                : Enemies.Count > 0
                    ? $"{DebugTargetIds.Enemy}{DebugTargetIds.Separator}{Enemies[0].Position}"
                    : DebugTargetIds.Player;
            _targetInitialized = true;
            _observedPlayer = Player;
            _observedEnemies = Enemies;
            return;
        }

        bool targetChanged = EnsureSelectedTargetAvailable();
        bool selectedChanged = targetChanged || SelectedSnapshotChanged();
        _observedPlayer = Player;
        _observedEnemies = Enemies;
        if (!selectedChanged)
            return;

        if (_loading)
        {
            _refreshPending = true;
        }
        else
        {
            await InspectSelectedAsync(preserveContent: true);
        }
    }

    /// <summary>
    /// Moves inspection to an available live target when the selected target disappears.
    /// </summary>
    /// <returns><see langword="true"/> when a replacement target was selected.</returns>
    private bool EnsureSelectedTargetAvailable()
    {
        string[] parts = _selectedTarget.Split(DebugTargetIds.Separator, DebugTargetIds.EnemySegmentCount);
        if (parts[0] == DebugTargetIds.Player)
        {
            if (Player is not null)
                return false;

            _selectedTarget = Enemies.Count > 0
                ? $"{DebugTargetIds.Enemy}{DebugTargetIds.Separator}{Enemies[0].Position}"
                : DebugTargetIds.Player;

            return true;
        }

        bool enemyAvailable = parts[0] == DebugTargetIds.Enemy && parts.Length == DebugTargetIds.EnemySegmentCount && int.TryParse(parts[1], out int position)
            && Enemies.Any(enemy => enemy.Position == position);

        if (enemyAvailable)
            return false;

        _selectedTarget = Player is not null
            ? DebugTargetIds.Player
            : Enemies.Count > 0
                ? $"{DebugTargetIds.Enemy}{DebugTargetIds.Separator}{Enemies[0].Position}"
                : DebugTargetIds.Player;
        return true;
    }

    /// <summary>
    /// Determines whether the currently inspected live snapshot was replaced.
    /// </summary>
    /// <returns><see langword="true"/> when the selected live source changed.</returns>
    private bool SelectedSnapshotChanged()
    {
        string[] parts = _selectedTarget.Split(DebugTargetIds.Separator, DebugTargetIds.EnemySegmentCount);
        if (parts[0] == DebugTargetIds.Player)
            return !ReferenceEquals(Player, _observedPlayer);

        if (parts[0] != DebugTargetIds.Enemy || parts.Length != DebugTargetIds.EnemySegmentCount || !int.TryParse(parts[1], out int position))
            return false;

        EnemyPokemonSnapshot? current = Enemies.FirstOrDefault(enemy => enemy.Position == position);
        EnemyPokemonSnapshot? observed = _observedEnemies.FirstOrDefault(enemy => enemy.Position == position);
        return !ReferenceEquals(current, observed);
    }

    /// <summary>
    /// Loads the initial combined inspector snapshot after the component first renders.
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
    /// <returns>A task representing the inspection and lookup requests.</returns>
    private async Task SelectTarget(ChangeEventArgs args)
    {
        _selectedTarget = args.Value?.ToString() ?? DebugTargetIds.Player;
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
    /// Opens a related Pokemon in the authorized active-run lookup.
    /// </summary>
    /// <param name="speciesId">The selected stable Pokemon identifier.</param>
    /// <returns>A task representing the page selection.</returns>
    private Task OpenLookupAsync(string speciesId)
    {
        _requestedLookupSpeciesId = speciesId;
        return SelectPageAsync(DebugInspectorPage.Lookup);
    }

    /// <summary>
    /// Requests both instance-specific and complete generated data for the selected current Pokemon.
    /// </summary>
    /// <param name="preserveContent">Whether an automatic refresh keeps the current content visible.</param>
    /// <param name="refreshLookup">Whether the shared overview lookup must also be refreshed.</param>
    /// <returns>A task representing both requests.</returns>
    private async Task InspectSelectedAsync(bool preserveContent = false, bool refreshLookup = true)
    {
        if (_loading)
            return;

        _loading = true;
        _backgroundRefresh = preserveContent && _pokemon is not null && _lookup is not null;
        _error = null;
        try
        {
            DebugPokemonInspectorSnapshot pokemon = await Connection.InspectPokemonAsync(CreateInspectionRequest());
            _pokemon = pokemon;
            if (refreshLookup)
                _lookup = await Connection.LookupDebugPokemonAsync(pokemon.SpeciesId);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            if (!preserveContent)
            {
                _pokemon = null;
                _lookup = null;
            }

            _error = exception.Message;
        }
        finally
        {
            _loading = false;
            _backgroundRefresh = false;
        }

        if (_refreshPending)
        {
            _refreshPending = false;
            await InspectSelectedAsync(preserveContent: true);
        }
    }

    /// <summary>
    /// Refreshes live instance diagnostics for the newly selected shared information page.
    /// </summary>
    /// <param name="page">The selected shared information page.</param>
    /// <returns>A task representing the refresh.</returns>
    private async Task SelectPokemonInformationPageAsync(PokemonInformationPage page)
    {
        _selectedPokemonPage = page;
        if (_loading)
        {
            _refreshPending = true;
            return;
        }

        await InspectSelectedAsync(preserveContent: true, refreshLookup: false);
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
        string[] parts = _selectedTarget.Split(DebugTargetIds.Separator, DebugTargetIds.EnemySegmentCount);
        return parts[0] switch
        {
            DebugTargetIds.Player => new DebugPokemonInspectionRequestPayload { Target = DebugPokemonTarget.Player, Section = (PokemonLookupSection)(int)_selectedPokemonPage },
            DebugTargetIds.Enemy => new DebugPokemonInspectionRequestPayload { Target = DebugPokemonTarget.Enemy, EnemyPosition = int.Parse(parts[1]), Section = (PokemonLookupSection)(int)_selectedPokemonPage },
            _ => throw new InvalidOperationException(Text["Debug.Inspector.InspectionTargetUnavailable"])
        };
    }

    /// <summary>
    /// Gets the CSS class for one debug page button.
    /// </summary>
    /// <param name="page">The represented page.</param>
    /// <returns>The page button CSS classes.</returns>
    private string GetPageClass(DebugInspectorPage page)
        => page == _selectedPage ? TrackerUiConstants.SelectedCssClass : string.Empty;

}
