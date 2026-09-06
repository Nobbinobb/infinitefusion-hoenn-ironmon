using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Starter;

/// <summary>
/// Presents revealed starter choices and submits a confirmed choice to its owning game scene.
/// </summary>
public partial class StarterSelection
{
    private const string _acceptedKey = "Starter.Selection.Accepted";
    private const string _rejectedKey = "Starter.Selection.Rejected";
    private const string _failedKey = "Starter.Selection.RequestFailed";

    private StarterChoiceSnapshot? _pending;
    private string? _selectionId;
    private string? _status;
    private bool _busy;
    private bool _accepted;

    /// <summary>
    /// Gets the connected-game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Requests { get; set; } = null!;

    /// <summary>
    /// Gets or sets the authoritative starter-selection snapshot.
    /// </summary>
    [Parameter, EditorRequired]
    public required StarterSelectionSnapshot Selection { get; set; }

    /// <summary>
    /// Gets or sets the game installation directory used for sprites.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Clears confirmations when a different scene opens or the selected slot becomes unavailable.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (_selectionId != Selection.SelectionId)
        {
            _selectionId = Selection.SelectionId;
            _pending = null;
            _status = null;
            _busy = false;
            _accepted = false;
        }

        if (_pending is not null && !Selection.Choices.Any(choice => choice.Index == _pending.Index && choice.CanSelect))
            _pending = null;
    }

    /// <summary>
    /// Checks the visible and authoritative selection requirements before presenting an action.
    /// </summary>
    /// <param name="choice">The starter slot.</param>
    /// <returns>Whether a new choice can be submitted.</returns>
    private bool CanChoose(StarterChoiceSnapshot choice)
    {
        return !_busy && !_accepted && Selection.Active && !string.IsNullOrEmpty(Selection.SelectionId) && choice.Revealed && choice.CanSelect
            && (Selection.MaximumBaseStatTotal is null || choice.BstEligible)
            && (!Selection.AutoSelect || choice.Favorite || choice.Index == Selection.RandomPickIndex);
    }

    /// <summary>
    /// Opens confirmation for an available choice.
    /// </summary>
    /// <param name="choice">The clicked revealed slot.</param>
    private void RequestChoice(StarterChoiceSnapshot choice)
    {
        if (CanChoose(choice))
            _pending = choice;
    }

    /// <summary>
    /// Dismisses an unsubmitted confirmation.
    /// </summary>
    private void CancelChoice()
        => _pending = null;

    /// <summary>
    /// Submits the reviewed slot without allowing a later scene to receive stale feedback.
    /// </summary>
    /// <returns>A task representing the game response.</returns>
    private async Task ConfirmChoiceAsync()
    {
        if (_pending is null || !CanChoose(_pending))
            return;

        string selectionId = Selection.SelectionId!;
        StarterSelectionRequestPayload request = new() { SelectionId = selectionId, Index = _pending.Index };
        _busy = true;
        _status = null;
        try
        {
            StarterSelectionResponsePayload response = await Requests.SelectStarterAsync(request);
            if (Selection.SelectionId != selectionId)
                return;

            _accepted = response.Accepted;
            _pending = null;
            _status = Text[response.Accepted ? _acceptedKey : _rejectedKey];
        }
        catch (Exception)
        {
            if (Selection.SelectionId == selectionId)
            {
                _pending = null;
                _status = Text[_failedKey];
            }
        }
        finally
        {
            if (Selection.SelectionId == selectionId)
                _busy = false;
        }
    }
}
