using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Presents one startup stage and an independent activity indicator while preparation owns the tracker window.
/// </summary>
public partial class StartupPreparationPanel : IDisposable
{
    private const string _progressKey = "Startup.Preparation.Progress";
    private const string _failedTitleKey = "Startup.Preparation.FailedTitle";
    private const string _failedDescriptionKey = "Startup.Preparation.FailedDescription";
    private const string _loadingKey = "Startup.Preparation.Loading";
    private const string _worldKey = "Startup.Preparation.World";
    private const string _pokemonKey = "Startup.Preparation.Pokemon";
    private const string _fusionKey = "Startup.Preparation.Fusions";
    private const string _checkingKey = "Startup.Preparation.Checking";
    private const string _finishingKey = "Startup.Preparation.Finishing";
    private const string _fallbackKey = "Startup.Preparation.Other";
    private const string _backKey = "Startup.Preparation.Back";
    private const string _showFunction = "ironmonStartup.show";
    private const string _closeFunction = "ironmonStartup.close";
    private const string _loadingPhase = "loading_save";
    private const string _worldPhase = "world_data";
    private const string _fusionPoolPhase = "fusion_pool";
    private const string _targetDataPhase = "target_data";
    private const string _maximumRangePhase = "maximum_range";
    private const string _strengthOrderPhase = "strength_order";
    private const string _strengthPairingPhase = "strength_pairing";
    private const string _strengthRepairPhase = "strength_repair";
    private const string _strengthRepairChainPhase = "strength_repair_chain";
    private const string _strengthValidationPhase = "strength_validation";
    private const string _resultIndexPhase = "result_index";
    private const string _preparedPhase = "prepared";
    private const string _completePhase = "complete";
    private TrackerStartupPreparationSnapshot _snapshot = TrackerStartupPreparationSnapshot.Idle;
    private bool _disposed;
    private bool _dialogShown;
    private ElementReference _dialog;

    /// <summary>
    /// Gets or initializes startup preparation observations from the connected game.
    /// </summary>
    [Inject]
    private TrackerConnectionState ConnectionState { get; set; } = null!;

    /// <summary>
    /// Gets the browser services available after interactive rendering begins.
    /// </summary>
    [Inject]
    private IServiceProvider Services { get; set; } = null!;

    /// <summary>
    /// Gets whether preparation or its terminal failure should remain visible.
    /// </summary>
    private bool IsVisible => _snapshot.Status is TrackerStartupPreparationStatus.Running or TrackerStartupPreparationStatus.Failed;

    /// <summary>
    /// Gets whether the game reported a preparation failure.
    /// </summary>
    private bool IsFailed => _snapshot.Status == TrackerStartupPreparationStatus.Failed;

    /// <summary>
    /// Gets friendly stage text without displaying internal phase identifiers.
    /// </summary>
    private string PhaseText => Text[_snapshot.Phase switch
    {
        _loadingPhase => _loadingKey,
        _worldPhase => _worldKey,
        _fusionPoolPhase or _targetDataPhase or _maximumRangePhase => _pokemonKey,
        _strengthOrderPhase or _strengthPairingPhase or _strengthRepairPhase or _strengthRepairChainPhase => _fusionKey,
        _strengthValidationPhase => _checkingKey,
        _resultIndexPhase or _preparedPhase or _completePhase => _finishingKey,
        _ => _fallbackKey
    }];

    /// <summary>
    /// Observes preparation independently of the currently selected tracker page.
    /// </summary>
    protected override void OnInitialized()
    {
        ConnectionState.StartupPreparation.Changed += HandlePreparationChanged;
        _snapshot = ConnectionState.StartupPreparation.Snapshot;
    }

    /// <summary>
    /// Opens a native modal above the complete tracker without moving or replacing its existing views.
    /// </summary>
    /// <param name="firstRender">Whether this is the first completed render.</param>
    /// <returns>Completion after applying native focus isolation or restoring the previous focus.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        bool show = IsVisible;
        if (_disposed || _dialogShown == show || Services.GetService(typeof(IJSRuntime)) is not IJSRuntime javaScript)
            return;

        await javaScript.InvokeVoidAsync(show ? _showFunction : _closeFunction, _dialog);
        _dialogShown = show;
        if (!_disposed && IsVisible != show)
            StateHasChanged();
    }

    /// <summary>
    /// Returns to the tracker after a failed load while keeping that attempt retired.
    /// </summary>
    private void DismissFailure()
        => ConnectionState.StartupPreparation.DismissFailure();

    /// <summary>
    /// Updates stage text only when a game preparation observation changes.
    /// </summary>
    /// <param name="sender">The shared preparation state.</param>
    /// <param name="args">The preparation change notification.</param>
    private void HandlePreparationChanged(object? sender, EventArgs args)
    {
        if (_disposed)
            return;

        _ = InvokeAsync(() =>
        {
            if (_disposed)
                return;

            _snapshot = ConnectionState.StartupPreparation.Snapshot;
            StateHasChanged();
        });
    }

    /// <summary>
    /// Removes the preparation subscription without keeping any background timer or polling task.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        ConnectionState.StartupPreparation.Changed -= HandlePreparationChanged;
        GC.SuppressFinalize(this);
    }
}
