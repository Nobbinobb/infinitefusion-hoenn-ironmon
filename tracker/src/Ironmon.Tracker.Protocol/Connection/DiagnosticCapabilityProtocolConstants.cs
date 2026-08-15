namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Defines framing bounds for diagnostic capability negotiation.
/// </summary>
public static class DiagnosticCapabilityProtocolConstants
{
    /// <summary>
    /// Gets the maximum number of capability identifiers accepted in one message.
    /// </summary>
    public const int MaximumCapabilityCount = 128;

    /// <summary>
    /// Gets the maximum accepted capability-identifier length.
    /// </summary>
    public const int MaximumCapabilityIdLength = 128;
}
