namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Replaces the named diagnostic capabilities granted to the connected game.
/// </summary>
public sealed class DiagnosticAccessChangedPayload
{
    /// <summary>
    /// Initializes an empty diagnostic-access replacement payload.
    /// </summary>
    public DiagnosticAccessChangedPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the complete effective capability set.
    /// </summary>
    public IReadOnlyList<string> DiagnosticCapabilities { get; init; } = [];
}
