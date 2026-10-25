using Microsoft.Maui.Platform;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Ironmon.Tracker.App.Seeds;

/// <summary>
/// Saves seeded-run tokens through the native Windows file picker.
/// </summary>
public sealed class SeedTokenFileSaver
{
    /// <summary>
    /// Prompts for a destination and writes one seeded-run token as UTF-8 text.
    /// </summary>
    /// <param name="token">The complete compact seed token.</param>
    /// <param name="seed">The run seed used in the suggested file name.</param>
    /// <returns><see langword="true"/> when a file was saved; otherwise <see langword="false"/> when the picker was cancelled.</returns>
    public async Task<bool> SaveAsync(string token, long seed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if(Application.Current is null)
            throw new InvalidOperationException("The tracker window is unavailable.");

        Window window = Application.Current.Windows[0] ?? throw new InvalidOperationException("The tracker window is unavailable.");
        Microsoft.UI.Xaml.Window platformWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("The tracker window is not ready.");

        FileSavePicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"ironmon-seed-{seed}"
        };

        picker.FileTypeChoices.Add("Ironmon seeded run", [SeedTokenConstants.FileExtension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, platformWindow.GetWindowHandle());
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
            return false;

        await FileIO.WriteTextAsync(file, token, Windows.Storage.Streams.UnicodeEncoding.Utf8);
        return true;
    }
}
