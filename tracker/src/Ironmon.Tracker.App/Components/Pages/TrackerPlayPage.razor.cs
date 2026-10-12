using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Pages;

/// <summary>
/// Provides Ironmon run creation and continuation at the connected game's title screen or main menu.
/// </summary>
public partial class TrackerPlayPage : IDisposable
{
    private const string SelectedStepClass = "selected";
    private const string SlotCssClass = "run-slot";
    private const string SelectedSlotCssClass = "run-slot selected";
    private const string StepAriaCurrent = "step";
    private readonly CancellationTokenSource _cancellation = new();
    private GameManagementPayload? _snapshot;
    private GameSaveSlotPayload? _selected;
    private List<IronmonRuleProfile> _profiles = RunProfileStore.Load();
    private string _wild = RunManagementProtocol.MixedPolicy;
    private string _trainer = RunManagementProtocol.MixedPolicy;
    private string _unfusion = RunManagementProtocol.RandomUnfusion;
    private string _profileName = string.Empty;
    private string? _status;
    private int _profileIndex;
    private int _step = 1;
    private bool _creating;
    private bool _busy;
    private bool _confirm;

    /// <summary>
    /// Gets whether the connected game is ready for run management at the title screen or main menu.
    /// </summary>
    private bool CanManageRuns => ConnectionState.Snapshot.Status == TrackerConnectionStatus.Connected && _snapshot?.State is RunManagementProtocol.MainMenuState or RunManagementProtocol.TitleScreenState;

    /// <summary>
    /// Starts title-state polling for the mounted page.
    /// </summary>
    protected override void OnInitialized()
    {
        ConnectionState.Changed += ConnectionChanged;
        _ = PollAsync();
    }

    /// <summary>
    /// Rerenders run availability when the shared connection state changes.
    /// </summary>
    /// <param name="sender">The shared connection state.</param>
    /// <param name="args">The connection change notification.</param>
    private void ConnectionChanged(object? sender, EventArgs args) => _ = InvokeAsync(StateHasChanged);

    /// <summary>
    /// Refreshes game-owned slot and menu state without simulating game input.
    /// </summary>
    /// <returns>A task representing polling until the page is disposed.</returns>
    private async Task PollAsync()
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(2));
        try
        {
            do
            {
                if (ConnectionState.Snapshot.Status == TrackerConnectionStatus.Connected && !_busy)
                {
                    try
                    {
                        GameManagementPayload next = await Requests.GetGameManagementAsync(_cancellation.Token);
                        await InvokeAsync(() =>
                        {
                            if (_snapshot?.MenuId != next.MenuId)
                            {
                                _creating = false;
                                _confirm = false;
                                _selected = null;
                            }
                            _snapshot = next;
                            StateHasChanged();
                        });
                    }
                    catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException or TrackerProtocolException)
                    {
                        await InvokeAsync(() => { _snapshot = null; _status = error.Message; StateHasChanged(); });
                    }
                }
            } while (await timer.WaitForNextTickAsync(_cancellation.Token));
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
    }

    /// <summary>
    /// Opens the save-selection step.
    /// </summary>
    private void BeginNew()
    {
        _creating = true;
        _step = 1;
        _status = null;
        ApplyProfile();
    }

    /// <summary>
    /// Selects a real manual game slot.
    /// </summary>
    /// <param name="slot">The current slot snapshot.</param>
    private void SelectSlot(GameSaveSlotPayload slot)
        => _selected = slot;

    /// <summary>
    /// Advances the new-run wizard.
    /// </summary>
    private void Next()
        => _step++;

    /// <summary>
    /// Returns to the previous step or closes the wizard.
    /// </summary>
    private void Back()
    {
        if (_step > 1)
        {
            _step--;
        }
        else
        {
            _creating = false;
        }
    }

    /// <summary>
    /// Gets the selected-state CSS class for one wizard step.
    /// </summary>
    /// <param name="step">The displayed step.</param>
    /// <returns>The selected-state class for the active step, or null for another step.</returns>
    private string? StepClass(int step)
        => _step == step ? SelectedStepClass : null;

    /// <summary>
    /// Gets the CSS classes for one save-slot card.
    /// </summary>
    /// <param name="slot">The displayed slot.</param>
    /// <returns>The selected or unselected save-slot classes.</returns>
    private string SlotClass(GameSaveSlotPayload slot)
        => slot.Slot == _selected?.Slot ? SelectedSlotCssClass : SlotCssClass;

    /// <summary>
    /// Gets the display label for a species-policy identifier.
    /// </summary>
    /// <param name="policy">The wire identifier.</param>
    /// <returns>The custom-fusion or normal-species label, or the mixed-policy label for other identifiers.</returns>
    private static string PolicyLabel(string policy) => policy switch
    {
        RunManagementProtocol.FusionPolicy => "Custom fusions only",
        RunManagementProtocol.NormalPolicy => "Normal only",
        _ => "Mixed"
    };

    /// <summary>
    /// Gets the display label for an unfusion-rule identifier.
    /// </summary>
    /// <param name="setting">The saved or draft unfusion rule.</param>
    /// <returns>The random-component or player-choice label.</returns>
    private static string UnfusionLabel(string setting)
        => setting == RunManagementProtocol.RandomUnfusion ? "Random component" : "Player choice";

    /// <summary>
    /// Selects a stored profile and copies its rules.
    /// </summary>
    /// <param name="args">The change event containing the selected profile index.</param>
    private void SelectProfile(ChangeEventArgs args)
    {
        if (!int.TryParse(args.Value?.ToString(), out int index) || index < 0 || index >= _profiles.Count)
            return;

        _profileIndex = index;
        ApplyProfile();
    }

    /// <summary>
    /// Copies a template into this wizard's independent draft.
    /// </summary>
    private void ApplyProfile()
    {
        IronmonRuleProfile profile = _profiles[_profileIndex];
        _wild = profile.Rules.WildPolicy;
        _trainer = profile.Rules.TrainerPolicy;
        _unfusion = profile.Rules.UnfusionSetting;
        _profileName = profile.Name;
    }

    /// <summary>
    /// Creates an immutable copy of the draft rules.
    /// </summary>
    /// <returns>A new rule record containing the current draft selections.</returns>
    private IronmonRunRules Rules()
        => new() { WildPolicy = _wild, TrainerPolicy = _trainer, UnfusionSetting = _unfusion };

    /// <summary>
    /// Saves or updates a named template without editing any game save.
    /// </summary>
    private void SaveProfile()
    {
        string name = _profileName.Trim();
        if (name.Length is < 1 or > 40)
        {
            _status = "Enter a profile name of up to 40 characters.";
            return;
        }

        int index = _profiles.FindIndex(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 && _profiles.Count >= 32)
        {
            _status = "You can keep up to 32 profiles.";
            return;
        }

        IronmonRuleProfile profile = new() { Name = name, Rules = Rules() };
        if (index >= 0)
        {
            _profiles[index] = profile;
        }
        else
        {
            _profiles.Add(profile);
            index = _profiles.Count - 1;
        }

        _profileIndex = index;
        RunProfileStore.Save(_profiles);
        _status = "Profile saved. Existing runs keep their own rules.";
    }

    /// <summary>
    /// Opens overwrite confirmation for an occupied slot or requests a new run in an empty slot.
    /// </summary>
    /// <returns>A completed task when confirmation is required, or the task that requests the new run.</returns>
    private Task RequestStart()
    {
        if (_selected?.Occupied == true)
        {
            _confirm = true;
            return Task.CompletedTask;
        }

        return StartAsync(false);
    }

    /// <summary>
    /// Closes overwrite confirmation without changing a save.
    /// </summary>
    private void CancelConfirm()
        => _confirm = false;

    /// <summary>
    /// Requests a new Ironmon run with the current draft rules and overwrite choice.
    /// </summary>
    /// <param name="overwrite">Whether the player confirmed replacement.</param>
    /// <returns>A task representing the run-management request and status update.</returns>
    private Task StartAsync(bool overwrite)
        => ExecuteAsync(RunManagementProtocol.StartCommand, _selected!, overwrite, Rules());

    /// <summary>
    /// Requests continuation of the selected Ironmon save.
    /// </summary>
    /// <param name="slot">The selected saved run.</param>
    /// <returns>A task representing the run-management request and status update.</returns>
    private Task ContinueAsync(GameSaveSlotPayload slot)
        => ExecuteAsync(RunManagementProtocol.ContinueCommand, slot, false, null);

    /// <summary>
    /// Sends a request for the reviewed menu and save revision, then displays acceptance or failure.
    /// </summary>
    /// <param name="command">The title action.</param>
    /// <param name="slot">The reviewed save snapshot.</param>
    /// <param name="overwrite">Explicit replacement consent.</param>
    /// <param name="rules">New-run rules, when creating.</param>
    /// <returns>A task representing the request and its displayed outcome.</returns>
    private async Task ExecuteAsync(string command, GameSaveSlotPayload slot, bool overwrite, IronmonRunRules? rules)
    {
        _busy = true;
        try
        {
            GameManagementResponsePayload response = await Requests.ManageGameAsync(command, new() { MenuId = _snapshot?.MenuId, Slot = slot.Slot, Revision = slot.Revision, Overwrite = overwrite, Rules = rules }, _cancellation.Token);
            _status = response.Accepted ? "Opening the Ironmon run in the game…" : "The game did not accept this action.";
            _confirm = false;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException or TrackerProtocolException)
        {
            _status = error.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Unsubscribes from connection changes and cancels polling when the page is disposed.
    /// </summary>
    public void Dispose()
    {
        ConnectionState.Changed -= ConnectionChanged;
        _cancellation.Cancel();
    }
}
