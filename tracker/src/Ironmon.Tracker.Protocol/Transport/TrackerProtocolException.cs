namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Represents invalid framing, JSON, message semantics, or a rejected tracker request.
/// </summary>
public sealed class TrackerProtocolException : Exception
{
    /// <summary>
    /// Gets the stable game-provided error code when the exception represents a rejected request.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Initializes a protocol exception with a message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public TrackerProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a protocol exception for a request rejected with a stable error code.
    /// </summary>
    /// <param name="errorCode">The stable game-provided error code.</param>
    /// <param name="message">The exception message.</param>
    public TrackerProtocolException(string? errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a protocol exception with a message and underlying exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The exception that caused this failure.</param>
    public TrackerProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
