using Microsoft.AspNetCore.Components;
using Ironmon.Tracker.App.Components.Lookup;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Coordinates combined current-Pokemon inspection, generated lookup, and run diagnostics.
/// </summary>
public partial class DebugInspector : IDisposable
{
    /// <summary>
    /// Gets or sets whether this inspector supplies its own heading when hosted independently.
    /// </summary>
    [Parameter]
    public bool ShowHeading { get; set; } = true;

    private DebugInspectorPage _selectedPage = DebugInspectorPage.Pokemon;
    private PokemonInformationPage _selectedPokemonPage = PokemonInformationPage.Overview;
    private DebugPokemonInspectorSnapshot? _pokemon;
    private PokemonLookupSnapshot? _lookup;
    private DebugRunDiagnosticsSnapshot? _diagnostics;
    private IReadOnlyList<DebugInspectorPage> _visiblePages = [];
    private string _selectedTarget = DebugTargetIds.Player;
    private string? _requestedLookupSpeciesId;
    private string? _error;
    private bool _loading;
    private bool _backgroundRefresh;
    private bool _targetInitialized;
    private bool _refreshPending;
    private CancellationTokenSource? _inspectionCancellation;
    private PlayerPokemonSnapshot? _observedPlayer;
    private IReadOnlyList<EnemyPokemonSnapshot> _observedEnemies = [];

    /// <summary>
    /// Gets or initializes the active game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Connection { get; set; } = null!;

    /// <summary>
    /// Gets or initializes tracker-owned diagnostic access.
    /// </summary>
    [Inject]
    private DiagnosticAccessService AccessService { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the shared connection state.
    /// </summary>
    [Inject]
    private TrackerConnectionState ConnectionState { get; set; } = null!;

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
    /// Subscribes to capability and connection changes affecting visible diagnostic pages.
    /// </summary>
    protected override void OnInitialized()
    {
        _selectedPokemonPage = GetFirstAuthorizedPokemonPage();
        RefreshVisiblePages();
        AccessService.Changed += HandleAuthorizationChanged;
        ConnectionState.Changed += HandleAuthorizationChanged;
    }

    /// <summary>
    /// Selects the initial available inspection target without overriding user selection.
    /// </summary>
    /// <returns>A task representing any inspection refresh required by new parameters.</returns>
    protected override async Task OnParametersSetAsync()
    {
        if (!_targetInitialized)
        {
            _selectedTarget = GetFirstAvailableTarget();
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

        if (_selectedPage != DebugInspectorPage.Pokemon)
            return;

        if (!IsSelectedTargetAvailable())
        {
            _inspectionCancellation?.Cancel();
            _refreshPending = false;
            _pokemon = null;
            _lookup = null;
            _error = null;
            return;
        }

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
            if (Player is not null && Connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonCurrentPlayer))
                return false;

            _selectedTarget = GetFirstAvailableTarget();

            return true;
        }

        bool enemyAvailable = Connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonCurrentEnemies)
            && parts[0] == DebugTargetIds.Enemy && parts.Length == DebugTargetIds.EnemySegmentCount && int.TryParse(parts[1], out int position)
            && Enemies.Any(enemy => enemy.Position == position);

        if (enemyAvailable)
            return false;

        _selectedTarget = GetFirstAvailableTarget();
        return true;
    }

    /// <summary>
    /// Gets the first live Pokemon target allowed by availability capabilities.
    /// </summary>
    /// <returns>The stable target selector value.</returns>
    private string GetFirstAvailableTarget()
    {
        if (Player is not null && Connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonCurrentPlayer))
            return DebugTargetIds.Player;

        if (Enemies.Count > 0 && Connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonCurrentEnemies))
            return DebugTargetIds.CreateEnemy(Enemies[0].Position);

        return DebugTargetIds.Player;
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

        if (IsSelectedTargetAvailable())
            await InspectSelectedAsync();

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Stores and immediately inspects the newly selected debug target.
    /// </summary>
    /// <param name="selectedTarget">The stable selected target value.</param>
    /// <returns>A task representing the inspection and lookup requests.</returns>
    private async Task SelectTarget(string selectedTarget)
    {
        _selectedTarget = selectedTarget;
        await InspectSelectedAsync();
    }

    /// <summary>
    /// Selects a debug page and loads diagnostics when required.
    /// </summary>
    /// <param name="page">The requested debug page.</param>
    /// <returns>A task representing any required request.</returns>
    private async Task SelectPageAsync(DebugInspectorPage page)
    {
        if (!_visiblePages.Contains(page))
            return;

        _selectedPage = page;
        _error = null;
        if (_loading)
            return;

        if (page == DebugInspectorPage.Pokemon)
        {
            EnsureSelectedTargetAvailable();
            await InspectSelectedAsync();
        }
        else if (page == DebugInspectorPage.Diagnostics)
        {
            await LoadDiagnosticsAsync();
        }
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

        if (!IsSelectedTargetAvailable())
        {
            _pokemon = null;
            _lookup = null;
            _error = null;
            return;
        }

        using CancellationTokenSource cancellation = new();
        _inspectionCancellation = cancellation;
        _loading = true;
        _backgroundRefresh = preserveContent && _pokemon is not null && _lookup is not null;
        _error = null;
        try
        {
            DebugPokemonInspectorSnapshot pokemon = await Connection.InspectPokemonAsync(CreateInspectionRequest(), cancellation.Token);
            _pokemon = pokemon;
            if (refreshLookup)
                _lookup = pokemon.Lookup ?? throw new TrackerProtocolException("The live Pokemon inspector response is missing its authorized lookup section.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
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
            if (ReferenceEquals(_inspectionCancellation, cancellation))
                _inspectionCancellation = null;

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
        if (!CanShowPokemonInformationPage(page))
            return;

        _selectedPokemonPage = page;
        if (_loading)
        {
            _refreshPending = true;
            return;
        }

        await InspectSelectedAsync(preserveContent: true);
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
    /// Determines whether the selected live inspection target is present in the latest tracker state.
    /// </summary>
    /// <returns><see langword="true"/> when the selected target can be inspected.</returns>
    private bool IsSelectedTargetAvailable()
    {
        string[] parts = _selectedTarget.Split(DebugTargetIds.Separator, DebugTargetIds.EnemySegmentCount);
        if (parts[0] == DebugTargetIds.Player)
            return Player is not null && Connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonCurrentPlayer);

        return parts[0] == DebugTargetIds.Enemy
            && Connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonCurrentEnemies)
            && parts.Length == DebugTargetIds.EnemySegmentCount
            && int.TryParse(parts[1], out int position)
            && Enemies.Any(enemy => enemy.Position == position);
    }

    /// <summary>
    /// Gets whether one primary diagnostic page is currently authorized.
    /// </summary>
    /// <param name="page">The represented diagnostic page.</param>
    /// <returns>Whether the page may be shown.</returns>
    private bool CanShowPage(DebugInspectorPage page)
    {
        return page switch
        {
            DebugInspectorPage.Pokemon => CanShowCurrentPokemon,
            DebugInspectorPage.Lookup => TrackerDiagnosticCapabilityRules.CanUseActivePokemonLookup(Connection),
            DebugInspectorPage.Diagnostics => TrackerDiagnosticCapabilityRules.HasAnyRunDiagnostics(Connection),
            DebugInspectorPage.Protocol => TrackerDiagnosticCapabilityRules.HasAnyTrackerDiagnostics(AccessService.Snapshot),
            DebugInspectorPage.Development => HasAnyDevelopmentControl(),
            _ => false
        };
    }

    /// <summary>
    /// Gets whether current-Pokemon inspection has both availability and information access.
    /// </summary>
    private bool CanShowCurrentPokemon
        => TrackerDiagnosticCapabilityRules.HasAnyPokemonInformation(Connection)
            && (Connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonCurrentPlayer)
                || Connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonCurrentEnemies));

    /// <summary>
    /// Gets whether player inspection is currently authorized.
    /// </summary>
    private bool CanInspectPlayer => Connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonCurrentPlayer);

    /// <summary>
    /// Gets whether enemy inspection is currently authorized.
    /// </summary>
    private bool CanInspectEnemies => Connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonCurrentEnemies);

    /// <summary>
    /// Gets whether at least one primary diagnostic page is authorized.
    /// </summary>
    private bool HasAnyPage => _visiblePages.Count > 0;

    /// <summary>
    /// Gets whether at least one gameplay-changing development control is authorized.
    /// </summary>
    private bool HasAnyDevelopmentControl()
    {
        return Connection.HasDiagnosticCapability(DiagnosticCapabilities.DevelopmentAutoRevive)
            || Connection.HasDiagnosticCapability(DiagnosticCapabilities.DevelopmentChangeAbility)
            || Connection.HasDiagnosticCapability(DiagnosticCapabilities.DevelopmentChangeMoves)
            || Connection.HasDiagnosticCapability(DiagnosticCapabilities.DevelopmentEvolution)
            || Connection.HasDiagnosticCapability(DiagnosticCapabilities.DevelopmentFullHeal)
            || Connection.HasDiagnosticCapability(DiagnosticCapabilities.DevelopmentGiveItem)
            || Connection.HasDiagnosticCapability(DiagnosticCapabilities.DevelopmentLevel)
            || Connection.HasDiagnosticCapability(DiagnosticCapabilities.DevelopmentSwapPokemon);
    }

    /// <summary>
    /// Gets whether one shared Pokemon information page is authorized.
    /// </summary>
    /// <param name="page">The represented Pokemon page.</param>
    /// <returns>Whether the information capability is effective.</returns>
    private bool CanShowPokemonInformationPage(PokemonInformationPage page)
    {
        return page switch
        {
            PokemonInformationPage.Overview => TrackerDiagnosticCapabilityRules.HasAnyOverviewSurface(Connection),
            PokemonInformationPage.Evolutions => TrackerDiagnosticCapabilityRules.HasAnyEvolutionSurface(Connection),
            _ => Connection.HasDiagnosticCapability(TrackerDiagnosticCapabilityRules.GetPokemonInformationCapability(page))
        };
    }

    /// <summary>
    /// Gets the live source represented by the selected target.
    /// </summary>
    private DebugPokemonTarget SelectedDebugTarget
        => _selectedTarget.StartsWith(DebugTargetIds.Enemy, StringComparison.Ordinal) ? DebugPokemonTarget.Enemy : DebugPokemonTarget.Player;

    /// <summary>
    /// Gets the selected enemy battler position when the live source is an enemy.
    /// </summary>
    private int? SelectedDebugEnemyPosition
    {
        get
        {
            string[] parts = _selectedTarget.Split(DebugTargetIds.Separator, DebugTargetIds.EnemySegmentCount);
            return parts[0] == DebugTargetIds.Enemy && parts.Length == DebugTargetIds.EnemySegmentCount && int.TryParse(parts[1], out int position) ? position : null;
        }
    }

    /// <summary>
    /// Gets the first authorized Pokemon information page in stable UI order.
    /// </summary>
    /// <returns>The first authorized page, or Overview when no page is available.</returns>
    private PokemonInformationPage GetFirstAuthorizedPokemonPage()
        => Enum.GetValues<PokemonInformationPage>().FirstOrDefault(CanShowPokemonInformationPage);

    /// <summary>
    /// Rebuilds authorized primary pages and reconciles the current selection.
    /// </summary>
    private void RefreshVisiblePages()
    {
        _visiblePages = [.. Enum.GetValues<DebugInspectorPage>().Where(CanShowPage)];
        if (_visiblePages.Contains(_selectedPage))
            return;

        _selectedPage = _visiblePages.Count > 0 ? _visiblePages[0] : default;
    }

    /// <summary>
    /// Clears protected values and recomputes page selection after access changes.
    /// </summary>
    /// <param name="sender">The changed access or connection service.</param>
    /// <param name="args">The empty change arguments.</param>
    private void HandleAuthorizationChanged(object? sender, EventArgs args)
    {
        _inspectionCancellation?.Cancel();
        _pokemon = null;
        _lookup = null;
        _diagnostics = null;
        _requestedLookupSpeciesId = null;
        _error = null;
        _selectedPokemonPage = GetFirstAuthorizedPokemonPage();
        EnsureSelectedTargetAvailable();
        RefreshVisiblePages();
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Cancels an inspection request when the debug view is removed.
    /// </summary>
    public void Dispose()
    {
        AccessService.Changed -= HandleAuthorizationChanged;
        ConnectionState.Changed -= HandleAuthorizationChanged;
        _inspectionCancellation?.Cancel();
        _inspectionCancellation?.Dispose();
    }

}
