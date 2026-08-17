using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Ironmon.Tracker.App.Components.Seeds;

/// <summary>
/// Presents explicit seeded-run token export, validation, confirmation, and import status.
/// </summary>
public partial class SeededRunPage : IDisposable
{
    private TrackerConnectionSnapshot _connection = new(TrackerConnectionStatus.Stopped, null, null, null);
    private string _importToken = string.Empty;
    private string? _activeImportTokenId;
    private string? _operationMessage;
    private SeedTokenData? _validatedToken;
    private bool _operationSucceeded;
    private bool _exportBusy;
    private bool _busy;

    /// <summary>
    /// Gets or initializes the seeded-run token codec.
    /// </summary>
    [Inject]
    private SeedTokenCodec Codec { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the connected-game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Requests { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the current connection state.
    /// </summary>
    [Inject]
    private TrackerConnectionState ConnectionState { get; set; } = null!;

    /// <summary>
    /// Subscribes to connection and import-lifecycle changes.
    /// </summary>
    protected override void OnInitialized()
    {
        _connection = ConnectionState.Snapshot;
        ConnectionState.Changed += HandleConnectionChanged;
        Requests.SeededRunImportStatusChanged += HandleImportStatusChanged;
    }

    /// <summary>
    /// Gets whether an active game run is available for explicit seed sharing.
    /// </summary>
    private bool CanUseActiveRun => _connection.Status == TrackerConnectionStatus.Connected
        && _connection.CurrentState?.IronmonActive == true
        && !string.IsNullOrWhiteSpace(_connection.CurrentState.RunId);

    /// <summary>
    /// Gets whether a local action or accepted game transaction owns the sharing workflow.
    /// </summary>
    private bool OperationLocked => _busy || _exportBusy || _activeImportTokenId is not null;

    /// <summary>
    /// Requests the active recipe and creates a new share token only after the export action.
    /// </summary>
    private async Task<RunReproductionRecipePayload> ExportRecipeAsync()
        => await Requests.ExportSeededRunAsync();

    /// <summary>
    /// Serializes active-run export actions with destructive import actions.
    /// </summary>
    /// <param name="busy">Whether the export panel owns an operation.</param>
    private void HandleExportBusyChanged(bool busy) => _exportBusy = busy;

    /// <summary>
    /// Updates the pasted token and clears any prior confirmation.
    /// </summary>
    /// <param name="args">The text-input event.</param>
    private void UpdateImportToken(ChangeEventArgs args)
    {
        _importToken = args.Value?.ToString() ?? string.Empty;
        _validatedToken = null;
        ClearOperationMessage();
    }

    /// <summary>
    /// Loads a bounded seeded-run token file without validating or importing it yet.
    /// </summary>
    /// <param name="args">The selected browser file.</param>
    private async Task LoadImportFileAsync(InputFileChangeEventArgs args)
    {
        ClearOperationMessage();
        _validatedToken = null;
        try
        {
            await using Stream stream = args.File.OpenReadStream(SeedTokenConstants.MaximumTokenLength);
            using StreamReader reader = new(stream);
            _importToken = await reader.ReadToEndAsync();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            _operationMessage = Text["Seeds.Errors.LoadFailed"];
        }
    }

    /// <summary>
    /// Validates a pasted or loaded token locally before exposing the destructive action.
    /// </summary>
    private async Task ValidateImportAsync()
    {
        BeginOperation();
        _validatedToken = null;
        try
        {
            SeedTokenValidationResult result = await Codec.ValidateAsync(_importToken.Trim());
            if (!result.IsValid)
            {
                _operationMessage = GetValidationMessage(result.Status);
                return;
            }

            _validatedToken = result.Data;
            _operationSucceeded = true;
            _operationMessage = Text["Seeds.Messages.Validated"];
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Sends the normalized token inputs after explicit destructive confirmation.
    /// </summary>
    private async Task ConfirmImportAsync()
    {
        SeedTokenData? token = _validatedToken;
        if (token is null)
            return;

        BeginOperation();
        _activeImportTokenId = token.TokenId;
        try
        {
            SeededRunImportStatusPayload status = await Requests.ImportSeededRunAsync(new SeededRunImportRequestPayload
            {
                TokenId = token.TokenId,
                Seed = token.Seed,
                GameVersion = token.GameVersion,
                IronmonVersion = token.IronmonVersion,
                DataMode = token.DataMode,
                Configuration = token.Configuration,
                CompatibilityFingerprint = token.CompatibilityFingerprint
            });

            ApplyImportStatus(status);
            if (status.Status != SeededRunImportStatus.Rejected)
                _validatedToken = null;
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            _activeImportTokenId = null;
            _operationMessage = Text["Seeds.Errors.ImportFailed"];
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Refreshes active-run availability after a connection transition.
    /// </summary>
    /// <param name="sender">The connection-state publisher.</param>
    /// <param name="args">The empty event arguments.</param>
    private void HandleConnectionChanged(object? sender, EventArgs args)
    {
        _connection = ConnectionState.Snapshot;
        if (_connection.Status != TrackerConnectionStatus.Connected && _activeImportTokenId is not null)
        {
            _activeImportTokenId = null;
            _operationSucceeded = false;
            _operationMessage = Text["Seeds.Status.ConnectionLost"];
        }

        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Applies one asynchronous game-owned import lifecycle transition.
    /// </summary>
    /// <param name="status">The import status payload.</param>
    private void HandleImportStatusChanged(SeededRunImportStatusPayload status)
    {
        if (!string.Equals(_activeImportTokenId, status.TokenId, StringComparison.Ordinal))
            return;

        _ = InvokeAsync(() =>
        {
            ApplyImportStatus(status);
            StateHasChanged();
        });
    }

    /// <summary>
    /// Maps a game-owned lifecycle status to accurate interface feedback.
    /// </summary>
    /// <param name="status">The received lifecycle status.</param>
    private void ApplyImportStatus(SeededRunImportStatusPayload status)
    {
        _operationSucceeded = status.Status is SeededRunImportStatus.Accepted or SeededRunImportStatus.Queued or SeededRunImportStatus.Started;
        _operationMessage = status.Status switch
        {
            SeededRunImportStatus.Accepted => Text["Seeds.Status.Accepted"],
            SeededRunImportStatus.Queued => Text["Seeds.Status.Queued"],
            SeededRunImportStatus.Started => Text["Seeds.Status.Started"],
            SeededRunImportStatus.Rejected => Text["Seeds.Status.Rejected", status.Message],
            SeededRunImportStatus.Failed => Text["Seeds.Status.Failed", status.Message],
            _ => status.Message
        };

        if (status.Status is SeededRunImportStatus.Started or SeededRunImportStatus.Rejected or SeededRunImportStatus.Failed)
            _activeImportTokenId = null;
    }

    /// <summary>
    /// Gets localized validation feedback without exposing untrusted token text.
    /// </summary>
    /// <param name="status">The local validation outcome.</param>
    /// <returns>The bounded localized failure.</returns>
    private string GetValidationMessage(SeedTokenValidationStatus status) => status switch
    {
        SeedTokenValidationStatus.UnsupportedAlgorithm or SeedTokenValidationStatus.UnsupportedType => Text["Seeds.Errors.Unsupported"],
        SeedTokenValidationStatus.UnknownKey => Text["Seeds.Errors.UnknownKey"],
        SeedTokenValidationStatus.InvalidSignature => Text["Seeds.Errors.InvalidSignature"],
        SeedTokenValidationStatus.InvalidClaims => Text["Seeds.Errors.InvalidClaims"],
        _ => Text["Seeds.Errors.Malformed"]
    };

    /// <summary>
    /// Gets whether an exception represents a recoverable game-connection failure.
    /// </summary>
    /// <param name="exception">The caught exception.</param>
    /// <returns>Whether the operation should present connection-safe feedback.</returns>
    private static bool IsConnectionFailure(Exception exception)
        => exception is IOException or InvalidOperationException or TrackerProtocolException or TimeoutException;

    /// <summary>
    /// Starts one serialized interface operation and clears prior feedback.
    /// </summary>
    private void BeginOperation()
    {
        _busy = true;
        ClearOperationMessage();
    }

    /// <summary>
    /// Clears transient operation feedback.
    /// </summary>
    private void ClearOperationMessage()
    {
        _operationMessage = null;
        _operationSucceeded = false;
    }

    /// <summary>
    /// Removes lifecycle subscriptions when the page closes.
    /// </summary>
    public void Dispose()
    {
        ConnectionState.Changed -= HandleConnectionChanged;
        Requests.SeededRunImportStatusChanged -= HandleImportStatusChanged;
        GC.SuppressFinalize(this);
    }
}
