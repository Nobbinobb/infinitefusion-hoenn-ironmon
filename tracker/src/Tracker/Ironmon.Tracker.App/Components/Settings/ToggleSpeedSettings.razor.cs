using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Ironmon.Tracker.App.Components.Settings;

/// <summary>
/// Owns a draft editor and commits complete profiles only after explicit Save.
/// </summary>
public partial class ToggleSpeedSettings : IAsyncDisposable
{
    private const string AttachFunction = "ironmonToggleSpeed.attach";
    private const string DisposeFunction = "ironmonToggleSpeed.dispose";
    private const string ResourcePrefix = "Settings.ToggleSpeed.";
    private const string FailedKey = "Settings.ToggleSpeed.SaveFailed";
    private const string LabelNames = "Overworld|Battle|Preview|PressKey|EnterBattle|ReturnOverworld|Undo|Redo|Unsaved|Saved|Saving|Save|Incomplete|SaveFailed|TooMany";
    private static readonly string[] _labelNames = LabelNames.Split('|');
    private ElementReference _root;
    private DotNetObjectReference<ToggleSpeedSettings>? _reference;
    private bool _helpOpen;
    private bool _saving;

    /// <summary>
    /// Gets the browser bridge for the pointer-based editor.
    /// </summary>
    [Inject]
    private IJSRuntime JavaScript { get; set; } = null!;

    /// <summary>
    /// Gets the committed profile used by new connections.
    /// </summary>
    [Inject]
    private TrackerConnectionOptions Options { get; set; } = null!;

    /// <summary>
    /// Gets the live game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Requests { get; set; } = null!;

    /// <summary>
    /// Gets the game connection status.
    /// </summary>
    [Inject]
    private TrackerConnectionState ConnectionState { get; set; } = null!;

    /// <summary>
    /// Gets or sets the callback reporting a committed profile.
    /// </summary>
    [Parameter]
    public EventCallback Saved { get; set; }

    /// <summary>
    /// Mounts the editor from a detached copy of the last committed configuration.
    /// </summary>
    /// <param name="firstRender">Whether this is the initial render.</param>
    /// <returns>The browser initialization task.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        _reference = DotNetObjectReference.Create(this);
        Dictionary<string, string> labels = _labelNames.ToDictionary(name => char.ToLowerInvariant(name[0]) + name[1..], name => Text[ResourcePrefix + name].Value, StringComparer.Ordinal);
        await JavaScript.InvokeVoidAsync(AttachFunction, _root, TrackerJson.SerializePayload(Options.ToggleSpeed), _reference, labels);
    }

    /// <summary>
    /// Validates, applies, and persists a complete draft without changing the saved profile on rejection.
    /// </summary>
    /// <param name="draft">The candidate profile from the editor.</param>
    /// <returns>Null on success, otherwise an error displayed in the editor.</returns>
    [JSInvokable]
    public async Task<string?> SaveProfileAsync(JsonElement draft)
    {
        if (_saving)
            return Text[FailedKey];

        _saving = true;
        try
        {
            ToggleSpeedConfiguration profile = TrackerJson.DeserializePayload<ToggleSpeedConfiguration>(draft);
            ToggleSpeedValidation.Validate(profile);
            if (ConnectionState.Snapshot.Status == TrackerConnectionStatus.Connected)
            {
                TrackerSettingsPayload response = await Requests.UpdateSettingsAsync(new() { Features = Options.Features, AutomaticReset = Options.AutomaticReset, AutoSelectStarter = Options.AutoSelectStarter, MaximumStarterBaseStatTotal = Options.MaximumStarterBaseStatTotal, FavoriteSpeciesIds = Options.FavoriteSpeciesIds, ToggleSpeed = profile });
                profile = response.ToggleSpeed ?? throw new TrackerProtocolException("The game did not accept the toggle speed profile.");
            }

            TrackerToggleSpeedSettings.Save(profile);
            Options.ToggleSpeed = profile;
            await Saved.InvokeAsync();
            return null;
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException or TimeoutException or TrackerProtocolException)
        {
            return Text[FailedKey];
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>
    /// Releases browser subscriptions and the callback reference after the settings page closes.
    /// </summary>
    /// <returns>The browser cleanup task.</returns>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_reference is not null)
                await JavaScript.InvokeVoidAsync(DisposeFunction, _root);
        }
        catch (JSDisconnectedException)
        {
        }
        finally
        {
            _reference?.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
