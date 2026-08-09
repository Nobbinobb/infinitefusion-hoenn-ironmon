namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Defines stable numeric move-data sentinel values.
/// </summary>
public static class MoveDataConstants
{
    /// <summary>
    /// Gets the base-power value used by moves without direct damage.
    /// </summary>
    public const int NoBasePower = 0;

    /// <summary>
    /// Gets the accuracy value used by moves that bypass accuracy checks.
    /// </summary>
    public const int AlwaysHitsAccuracy = 0;
}
