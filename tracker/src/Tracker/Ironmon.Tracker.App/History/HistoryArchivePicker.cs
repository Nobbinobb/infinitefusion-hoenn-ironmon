using Microsoft.Maui.Platform;
using Windows.Storage.Pickers;

namespace Ironmon.Tracker.App.History;

/// <summary>
/// Uses native Windows pickers for history archives protected by the tracker.
/// </summary>
public sealed class HistoryArchivePicker : IHistoryArchivePicker
{
    private const string _extension = ".irmhistory";
    private const string _suggestedName = "ironmon-history";

    /// <summary>
    /// Gets the active native owner window for accessible picker placement.
    /// </summary>
    private static nint Owner => GetOwner();

    /// <summary>
    /// Opens the native archive destination picker.
    /// </summary>
    /// <returns>The selected absolute path, or null on cancellation.</returns>
    public async Task<string?> ChooseExportAsync()
    {
        FileSavePicker picker = new() { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = _suggestedName };
        picker.FileTypeChoices.Add("Encrypted Ironmon history", [_extension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Owner);
        return (await picker.PickSaveFileAsync())?.Path;
    }

    /// <summary>
    /// Opens the native existing-archive picker.
    /// </summary>
    /// <returns>The selected absolute path, or null on cancellation.</returns>
    public async Task<string?> ChooseImportAsync()
    {
        FileOpenPicker picker = new() { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(_extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Owner);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    /// <summary>
    /// Resolves the native owner window for the archive picker.
    /// </summary>
    /// <returns>The active native tracker window handle.</returns>
    /// <exception cref="InvalidOperationException"></exception>
    private static nint GetOwner()
    {
        if (Application.Current is null || Application.Current.Windows.Count == default || Application.Current.Windows[0].Handler?.PlatformView is not Microsoft.UI.Xaml.Window window)
            throw new InvalidOperationException("The tracker window is not ready.");

        return window.GetWindowHandle();
    }
}
