using Ironmon.Tracker.App.History;
using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Collection.History;
using Microsoft.AspNetCore.Components;
using System.Globalization;
using System.Security.Cryptography;

namespace Ironmon.Tracker.App.Components.Settings;

/// <summary>
/// Reviews, exports and restores unrelated history without exposing private achievement conditions.
/// </summary>
public partial class HistoryMaintenanceSettings : IDisposable
{
    private const string _megabyteFormat = "0.000";
    private string? _status;
    private HistoryMaintenanceResult? _review;
    private bool _busy;
    private bool _importing;
    private bool _disposed;

    /// <summary>
    /// Gets or initializes the serialized collection service.
    /// </summary>
    [Inject]
    private GameplayCollectionStore Collection { get; set; } = null!;
    /// <summary>
    /// Gets or initializes the run archive owning recipe-file synchronization.
    /// </summary>
    [Inject]
    private CompletedRunArchive Runs { get; set; } = null!;
    /// <summary>
    /// Gets or initializes native archive pickers.
    /// </summary>
    [Inject]
    private IHistoryArchivePicker Picker { get; set; } = null!;
    /// <summary>
    /// Gets or initializes live connection status.
    /// </summary>
    [Inject]
    private TrackerConnectionState Connection { get; set; } = null!;

    /// <summary>
    /// Gets whether maintenance is available after the game disconnects, including a failed connection.
    /// </summary>
    private bool CanMaintain => Connection.Snapshot.Status is TrackerConnectionStatus.Stopped or TrackerConnectionStatus.Waiting or TrackerConnectionStatus.Error;

    /// <summary>
    /// Subscribes to connection changes while this panel is visible.
    /// </summary>
    protected override void OnInitialized()
        => Connection.Changed += ConnectionChanged;

    /// <summary>
    /// Refreshes controls after a game connects or disconnects.
    /// </summary>
    /// <param name="sender">The connection state publisher.</param>
    /// <param name="args">The state-change notification.</param>
    private void ConnectionChanged(object? sender, EventArgs args)
    {
        if (!_disposed)
        {
            _ = InvokeAsync(() =>
            {
                if (!CanMaintain)
                    CancelReview();

                StateHasChanged();
            });
        }
    }

    /// <summary>
    /// Formats decimal megabytes rounded down so displayed savings never exceed measured bytes.
    /// </summary>
    /// <param name="bytes">The actual byte measurement.</param>
    /// <returns>The localized decimal MB value.</returns>
    private static string Megabytes(long bytes)
        => (bytes / 1000L / 1000m).ToString(_megabyteFormat, CultureInfo.CurrentCulture);

    /// <summary>
    /// Reports measured disk savings after accounting for the encrypted archive itself.
    /// </summary>
    /// <param name="result">The completed export's measured physical sizes.</param>
    /// <returns>The net disk-space result with an explicit same-drive assumption and any retained-file notice.</returns>
    private static string ExportStatus(HistoryMaintenanceResult result)
    {
        long net = result.RemovedBytes - result.ArchiveBytes;
        string change = net >= 0 ? $"{Megabytes(net)} MB disk space freed" : $"{Megabytes(-net)} MB additional disk space used";
        return $"Export complete: {change} with the archive on the same drive. Archive size: {Megabytes(result.ArchiveBytes)} MB. {result.Notice}";
    }

    /// <summary>
    /// Prepares an explicit export review without removing any data.
    /// </summary>
    /// <returns>The asynchronous storage inspection.</returns>
    private async Task ReviewAsync()
    {
        if (_busy || !CanMaintain)
            return;
        CancelReview();
        _busy = true;
        _status = "Measuring disk savings on a temporary copy…";
        TrackerConnectionSnapshot connection = Connection.Snapshot;
        try
        {
            _review = await Collection.InspectHistoryAsync();
            if (!CanMaintain || !ReferenceEquals(connection, Connection.Snapshot))
            {
                CancelReview();
                _status = "The game connection changed while measuring. Close the game and review the export again.";
                return;
            }

            _status = _review.EligibleBytes == 0 ? "No eligible history is currently available to export." : null;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or CryptographicException)
        {
            _status = error.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Opens the import confirmation without accessing a file or changing history.
    /// </summary>
    private void PrepareImport()
    {
        if (_busy || !CanMaintain)
            return;

        CancelReview();
        _importing = true;
        _status = null;
    }

    /// <summary>
    /// Clears the current export review or import confirmation.
    /// </summary>
    private void CancelReview()
    {
        _review = null;
        _importing = false;
    }

    /// <summary>
    /// Executes the reviewed operation after native file selection and rechecks game connection status.
    /// </summary>
    /// <returns>The verified maintenance operation and refreshed archive list.</returns>
    private async Task ExecuteAsync()
    {
        if (_busy || !CanMaintain || (_review is null && !_importing))
            return;

        bool importing = _importing;
        _busy = true;
        try
        {
            string? path = importing ? await Picker.ChooseImportAsync() : await Picker.ChooseExportAsync();
            if (path is null)
                return;

            if (!CanMaintain)
                throw new InvalidOperationException("The game connected while choosing a file. Close it before continuing.");

            _status = importing ? "Verifying and restoring history…" : "Compressing and verifying the archive before removing history…";
            StateHasChanged();
            HistoryMaintenanceResult result = await Runs.MaintainHistoryAsync(() => importing ? Collection.ImportHistoryAsync(path) : Collection.ExportHistoryAsync(path));
            _status = result.AlreadyImported ? "This archive has already been imported. No progress was counted twice."
                : importing ? "History restored. Newer gameplay and existing achievements were preserved."
                : ExportStatus(result);

            CancelReview();
        }
        catch (CryptographicException)
        {
            _status = "The archive could not be verified for this tracker profile. It may be damaged or belong to another profile. No history was imported.";
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _status = error.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Releases connection events when leaving settings.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        Connection.Changed -= ConnectionChanged;
    }
}
