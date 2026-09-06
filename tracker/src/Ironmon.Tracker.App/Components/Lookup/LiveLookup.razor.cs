using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Hosts world lookup and utility tools for the connected active run.
/// </summary>
public partial class LiveLookup : IDisposable
{
    private static readonly LiveLookupSection[] _sections = [LiveLookupSection.Encounters, LiveLookupSection.Trainers, LiveLookupSection.Items, LiveLookupSection.TypeCoverage];
    private TrackerConnectionSnapshot _connection = new(TrackerConnectionStatus.Stopped, null, null, null);
    private TrackerRunStateSnapshot _run = new(null, null);
    private readonly TypeCoverageSelectionState _coverageSelection = new();
    private LiveLookupSection _selectedSection = LiveLookupSection.Encounters;

    /// <summary>
    /// Gets or initializes the shared connection status service.
    /// </summary>
    [Inject]
    private TrackerConnectionState ConnectionState { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the shared request and preparation service.
    /// </summary>
    [Inject]
    private TrackerRequestClient Connection { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the shared live run state.
    /// </summary>
    [Inject]
    private TrackerRunState RunState { get; set; } = null!;

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Loads active-run availability and subscribes to connection changes.
    /// </summary>
    protected override void OnInitialized()
    {
        _connection = ConnectionState.Snapshot;
        _run = RunState.Snapshot;
        UpdateCoverageSelection();
        ConnectionState.Changed += HandleConnectionChanged;
        RunState.Changed += HandleRunChanged;
        Connection.ObtainabilityProgress.Changed += HandlePreparationChanged;
    }

    /// <summary>
    /// Gets whether the connected game exposes an active Ironmon run.
    /// </summary>
    /// <returns>Whether active-run lookup is available.</returns>
    private bool HasActiveRun()
    {
        return _connection.Status == TrackerConnectionStatus.Connected
            && _connection.CurrentState?.IronmonActive == true
            && _connection.CurrentState.CompletedRun is null
            && GetActiveRunId() is not null;
    }

    /// <summary>
    /// Gets the connected run identifier.
    /// </summary>
    /// <returns>The active run identifier, or null.</returns>
    private string? GetActiveRunId()
        => _connection.CurrentState?.RunId ?? _connection.Game?.RunId;

    /// <summary>
    /// Gets preparation for this active run, independent of archive selection.
    /// </summary>
    /// <returns>The run-specific preparation snapshot.</returns>
    private TrackerObtainabilityProgressSnapshot GetPreparationProgress()
        => Connection.ObtainabilityProgress.GetActiveRunSnapshot(GetActiveRunId());

    /// <summary>
    /// Reveals lookup tabs when automatic or explicit preparation finishes.
    /// </summary>
    /// <param name="sender">The shared preparation state.</param>
    /// <param name="args">The change event arguments.</param>
    private void HandlePreparationChanged(object? sender, EventArgs args)
    {
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Selects one active-run Lookup section.
    /// </summary>
    /// <param name="section">The selected section.</param>
    private void SelectSection(LiveLookupSection section)
    {
        _selectedSection = section;
    }

    /// <summary>
    /// Gets the localized name of one active-run Lookup section.
    /// </summary>
    /// <param name="section">The section to name.</param>
    /// <returns>The localized section name.</returns>
    private string GetSectionName(LiveLookupSection section) => section switch
    {
        LiveLookupSection.Trainers => Text["Lookup.Areas.Trainers"],
        LiveLookupSection.Encounters => Text["Lookup.Areas.Encounters"],
        LiveLookupSection.Items => Text["Lookup.Areas.Items"],
        LiveLookupSection.TypeCoverage => Text["Lookup.Coverage.Tab"],
        _ => string.Empty
    };

    /// <summary>
    /// Gets the selected section's area category.
    /// </summary>
    /// <returns>The applicable shared area category.</returns>
    /// <exception cref="InvalidOperationException">Thrown when Type Coverage is selected.</exception>
    private AreaContentCategory GetAreaCategory() => _selectedSection switch
    {
        LiveLookupSection.Trainers => AreaContentCategory.Trainer,
        LiveLookupSection.Encounters => AreaContentCategory.Encounter,
        LiveLookupSection.Items => AreaContentCategory.Item,
        _ => throw new InvalidOperationException("Type Coverage does not use an area category.")
    };

    /// <summary>
    /// Refreshes active-run availability after a connection change.
    /// </summary>
    /// <param name="sender">The connection state raising the event.</param>
    /// <param name="args">The connection change event arguments.</param>
    private void HandleConnectionChanged(object? sender, EventArgs args)
    {
        _connection = ConnectionState.Snapshot;
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Updates current damaging-move defaults after live player state changes.
    /// </summary>
    /// <param name="sender">The run-state service.</param>
    /// <param name="args">The empty change arguments.</param>
    private void HandleRunChanged(object? sender, EventArgs args)
    {
        _run = RunState.Snapshot;
        UpdateCoverageSelection();
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Applies the current player's unique Physical and Special move types to the retained selection state.
    /// </summary>
    private void UpdateCoverageSelection()
    {
        IReadOnlyList<string> moveTypes = TrackerTypeCoverageMoveSelector.SelectCurrentMoveTypes(_run.Player?.Moves ?? []);
        _coverageSelection.UpdateCurrentPokemon(_run.Player?.PokemonId, moveTypes);
    }

    /// <summary>
    /// Removes the connection-state subscription.
    /// </summary>
    public void Dispose()
    {
        ConnectionState.Changed -= HandleConnectionChanged;
        RunState.Changed -= HandleRunChanged;
        Connection.ObtainabilityProgress.Changed -= HandlePreparationChanged;
        GC.SuppressFinalize(this);
    }
}
