using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using System.Globalization;

namespace Ironmon.Tracker.App.Components.Access;

/// <summary>
/// Presents diagnostic-access activation, inspection, replacement, and removal.
/// </summary>
public partial class DiagnosticAccessPage : IDisposable
{
    private DiagnosticAccessSnapshot _snapshot = DiagnosticAccessSnapshot.None();
    private string _token = string.Empty;
    private string? _operationMessage;
    private bool _operationSucceeded;
    private bool _busy;

    /// <summary>
    /// Gets or initializes the tracker-owned diagnostic-access lifecycle service.
    /// </summary>
    [Inject]
    private DiagnosticAccessService AccessService { get; set; } = null!;

    /// <summary>
    /// Subscribes to effective-access changes.
    /// </summary>
    protected override void OnInitialized()
    {
        _snapshot = AccessService.Snapshot;
        AccessService.Changed += HandleAccessChanged;
    }

    /// <summary>
    /// Gets capabilities included by a broader direct grant.
    /// </summary>
    private IReadOnlyList<string> IncludedCapabilities
        => _snapshot.Grant is null ? [] : [.. _snapshot.Grant.EffectiveCapabilities.Except(_snapshot.Grant.DirectCapabilities, StringComparer.Ordinal)];

    /// <summary>
    /// Updates the pasted compact token.
    /// </summary>
    /// <param name="args">The text-input event.</param>
    private void UpdateToken(ChangeEventArgs args)
    {
        _token = args.Value?.ToString() ?? string.Empty;
        ClearOperationMessage();
    }

    /// <summary>
    /// Loads a compact token from the selected access file without activating it yet.
    /// </summary>
    /// <param name="args">The selected browser file.</param>
    /// <returns>A task representing bounded file loading.</returns>
    private async Task ImportTokenFileAsync(InputFileChangeEventArgs args)
    {
        ClearOperationMessage();
        try
        {
            await using Stream stream = args.File.OpenReadStream(DiagnosticAccessTokenConstants.MaximumTokenLength);
            using StreamReader reader = new(stream);
            _token = await reader.ReadToEndAsync();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            _operationMessage = Text["Access.Errors.ImportFailed"];
        }
    }

    /// <summary>
    /// Validates and persists the pasted or imported token.
    /// </summary>
    /// <returns>A task representing activation.</returns>
    private async Task ActivateAsync()
    {
        _busy = true;
        ClearOperationMessage();
        try
        {
            DiagnosticAccessValidationResult result = await AccessService.ActivateAsync(_token);
            if (!result.IsValid)
            {
                _operationMessage = GetValidationMessage(result.Status);
                return;
            }

            _token = string.Empty;
            _operationSucceeded = true;
            _operationMessage = Text["Access.Messages.Activated"];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _operationMessage = Text["Access.Errors.SaveFailed"];
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Removes the persisted token and all of its effective access.
    /// </summary>
    private void RemoveAccess()
    {
        _busy = true;
        ClearOperationMessage();
        try
        {
            AccessService.Remove();
            _token = string.Empty;
            _operationSucceeded = true;
            _operationMessage = Text["Access.Messages.Removed"];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _operationMessage = Text["Access.Errors.RemoveFailed"];
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Refreshes the page after access changes or expires.
    /// </summary>
    /// <param name="sender">The lifecycle service raising the event.</param>
    /// <param name="args">The empty change event arguments.</param>
    private void HandleAccessChanged(object? sender, EventArgs args)
    {
        _snapshot = AccessService.Snapshot;
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Gets the localized lifecycle state heading.
    /// </summary>
    /// <returns>The concise state heading.</returns>
    private string GetStateHeading() => _snapshot.State switch
    {
        DiagnosticAccessState.Active when _snapshot.IsLifetime => Text["Access.State.Lifetime"],
        DiagnosticAccessState.Active => Text["Access.State.Active"],
        DiagnosticAccessState.Expired => Text["Access.State.Expired"],
        DiagnosticAccessState.Invalid => Text["Access.State.Invalid"],
        DiagnosticAccessState.DeveloperOverride => Text["Access.State.Developer"],
        _ => Text["Access.State.None"]
    };

    /// <summary>
    /// Gets the localized lifecycle state description.
    /// </summary>
    /// <returns>The state explanation.</returns>
    private string GetStateDescription() => _snapshot.State switch
    {
        DiagnosticAccessState.Active when _snapshot.IsLifetime => Text["Access.State.LifetimeDescription"],
        DiagnosticAccessState.Active => Text["Access.State.ActiveDescription"],
        DiagnosticAccessState.Expired => Text["Access.State.ExpiredDescription"],
        DiagnosticAccessState.Invalid => Text["Access.State.InvalidDescription"],
        DiagnosticAccessState.DeveloperOverride => Text["Access.State.DeveloperDescription"],
        _ => Text["Access.State.NoneDescription"]
    };

    /// <summary>
    /// Gets the lifecycle state CSS class.
    /// </summary>
    /// <returns>The state modifier class.</returns>
    private string GetStateClass() => _snapshot.State switch
    {
        DiagnosticAccessState.Active or DiagnosticAccessState.DeveloperOverride => "active",
        DiagnosticAccessState.Expired or DiagnosticAccessState.Invalid => "problem",
        _ => "none"
    };

    /// <summary>
    /// Formats one signed expiration for local display.
    /// </summary>
    /// <param name="grant">The validated signed grant.</param>
    /// <returns>The lifetime label or local expiration value.</returns>
    private string FormatExpiration(DiagnosticAccessGrant grant)
        => grant.ExpiresAt is null ? Text["Access.Details.Never"] : grant.ExpiresAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>
    /// Gets the localized display name for one supported capability.
    /// </summary>
    /// <param name="capability">The stable capability identifier.</param>
    /// <returns>The localized capability name.</returns>
    private string GetCapabilityName(string capability)
        => Text[TrackerDiagnosticAccessLocalizationKeys.GetCapabilityName(capability)];

    /// <summary>
    /// Gets the localized activation failure for one validation status.
    /// </summary>
    /// <param name="status">The failed validation outcome.</param>
    /// <returns>The bounded user-facing failure.</returns>
    private string GetValidationMessage(DiagnosticAccessValidationStatus status) => status switch
    {
        DiagnosticAccessValidationStatus.Expired => Text["Access.Errors.Expired"],
        DiagnosticAccessValidationStatus.UnknownKey => Text["Access.Errors.UnknownKey"],
        DiagnosticAccessValidationStatus.UnsupportedAlgorithm or DiagnosticAccessValidationStatus.UnsupportedType => Text["Access.Errors.Unsupported"],
        DiagnosticAccessValidationStatus.InvalidSignature => Text["Access.Errors.InvalidSignature"],
        DiagnosticAccessValidationStatus.InvalidClaims => Text["Access.Errors.InvalidClaims"],
        _ => Text["Access.Errors.Malformed"]
    };

    /// <summary>
    /// Clears transient operation feedback.
    /// </summary>
    private void ClearOperationMessage()
    {
        _operationMessage = null;
        _operationSucceeded = false;
    }

    /// <summary>
    /// Removes the lifecycle subscription when the page closes.
    /// </summary>
    public void Dispose()
    {
        AccessService.Changed -= HandleAccessChanged;
        GC.SuppressFinalize(this);
    }
}
