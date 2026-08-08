namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Represents invalid framing, JSON, or semantics in a tracker protocol message.
/// </summary>
public sealed class TrackerProtocolException : Exception
{
    /// <summary>
    /// Initializes a protocol exception with a message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public TrackerProtocolException(string message)
        : base(message)
    {
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
