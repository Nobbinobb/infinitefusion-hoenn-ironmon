namespace Ironmon.Tracker.Protocol.Debug;

/// <summary>
/// Describes one displayed normal component of an inspected fusion.
/// </summary>
public sealed class DebugSpeciesSnapshot
{
    /// <summary>
    /// Initializes an empty debug species snapshot for protocol serialization.
    /// </summary>
    public DebugSpeciesSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the stable species identifier.
    /// </summary>
    public required string SpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the localized species name.
    /// </summary>
    public required string SpeciesName { get; init; }
}
