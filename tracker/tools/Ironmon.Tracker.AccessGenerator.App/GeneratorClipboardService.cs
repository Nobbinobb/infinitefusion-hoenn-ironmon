using DataPackage = Windows.ApplicationModel.DataTransfer.DataPackage;
using DataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;
using NativeClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;

namespace Ironmon.Tracker.AccessGenerator.App;

/// <summary>
/// Copies token text into the Windows clipboard independently of the generator lifetime.
/// </summary>
public static class GeneratorClipboardService
{
    /// <summary>
    /// Copies and flushes the token on the UI thread before reporting success.
    /// </summary>
    /// <param name="token">The compact access token to retain after the app closes.</param>
    /// <returns>The clipboard persistence task.</returns>
    public static Task CopyTokenAsync(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            DataPackage content = new() { RequestedOperation = DataPackageOperation.Copy };
            content.SetText(token);
            NativeClipboard.SetContent(content);
            NativeClipboard.Flush();
        });
    }
}
