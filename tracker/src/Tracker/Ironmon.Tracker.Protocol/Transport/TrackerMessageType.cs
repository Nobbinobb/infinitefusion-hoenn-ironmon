namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Identifies the semantic kind of a tracker protocol message.
/// </summary>
public enum TrackerMessageType
{
    /// <summary>
    /// Indicates an unsolicited game lifecycle or state update.
    /// </summary>
    Event = 0,

    /// <summary>
    /// Indicates a command sent by the tracker.
    /// </summary>
    Request = 1,

    /// <summary>
    /// Indicates the result of an earlier request.
    /// </summary>
    Response = 2
}
