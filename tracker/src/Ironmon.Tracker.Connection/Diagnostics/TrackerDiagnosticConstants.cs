namespace Ironmon.Tracker.Connection.Diagnostics;

/// <summary>
/// Defines stable diagnostic history limits and entry names.
/// </summary>
public static class TrackerDiagnosticConstants
{
    /// <summary>
    /// Gets the maximum retained diagnostic entry count.
    /// </summary>
    public const int EntryLimit = 200;

    /// <summary>
    /// Gets the listener-start lifecycle name.
    /// </summary>
    public const string Listening = "Listening";

    /// <summary>
    /// Gets the listener-stop lifecycle name.
    /// </summary>
    public const string Stopped = "Stopped";

    /// <summary>
    /// Gets the game-disconnection lifecycle name.
    /// </summary>
    public const string Disconnected = "Disconnected";

    /// <summary>
    /// Gets the handshake lifecycle name.
    /// </summary>
    public const string Handshaking = "Handshaking";

    /// <summary>
    /// Gets the connected lifecycle name.
    /// </summary>
    public const string Connected = "Connected";

    /// <summary>
    /// Gets the error lifecycle name.
    /// </summary>
    public const string Error = "Error";

    /// <summary>
    /// Gets the fallback event entry name.
    /// </summary>
    public const string EventFallback = "event";

    /// <summary>
    /// Gets the fallback request entry name.
    /// </summary>
    public const string RequestFallback = "request";

    /// <summary>
    /// Gets the successful response entry name.
    /// </summary>
    public const string Response = "response";

    /// <summary>
    /// Gets the failed response entry name.
    /// </summary>
    public const string ErrorResponse = "error_response";

    /// <summary>
    /// Gets the fallback unknown-message entry name.
    /// </summary>
    public const string MessageFallback = "message";
}
