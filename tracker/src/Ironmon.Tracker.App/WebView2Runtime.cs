using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace Ironmon.Tracker.App;

/// <summary>
/// Detects whether the Evergreen WebView2 runtime can host the tracker UI.
/// </summary>
internal static class WebView2Runtime
{
    /// <summary>
    /// Gets whether a usable WebView2 runtime is installed.
    /// </summary>
    /// <returns>Whether WebView2 reported an installed runtime version.</returns>
    internal static bool IsAvailable()
    {
        try
        {
            string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrWhiteSpace(version);
        }
        catch (Exception exception) when (IsMissingRuntimeException(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// Gets whether WebView2 failed because its native runtime is unavailable.
    /// </summary>
    /// <param name="exception">The runtime detection exception.</param>
    /// <returns>Whether the exception represents a missing WebView2 installation.</returns>
    private static bool IsMissingRuntimeException(Exception exception)
        => exception.GetType().Name == "WebView2RuntimeNotFoundException" || exception is COMException or DllNotFoundException or FileNotFoundException;
}
