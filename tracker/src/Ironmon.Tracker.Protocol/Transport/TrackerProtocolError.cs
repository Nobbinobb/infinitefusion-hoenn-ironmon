namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Describes an error returned in a failed protocol response.
/// </summary>
public sealed class TrackerProtocolError
{
    /// <summary>
    /// Initializes a protocol error.
    /// </summary>
    /// <param name="code">The stable machine-readable error code.</param>
    /// <param name="message">The human-readable error message.</param>
    /// <exception cref="ArgumentException">Thrown when a required value is empty.</exception>
    public TrackerProtocolError(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Message = message;
    }

    /// <summary>
    /// Gets the stable machine-readable error code.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets the human-readable error message.
    /// </summary>
    public string Message { get; }
}
