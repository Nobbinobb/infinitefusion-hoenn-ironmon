namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Defines stable identifiers used by completed-run move-access metrics.
/// </summary>
public static class MoveAccessMetricIdentifiers
{
    /// <summary>
    /// Gets the supported metrics schema version.
    /// </summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// Gets the level-up access channel identifier.
    /// </summary>
    public const string LearnsetChannel = "learnset";

    /// <summary>
    /// Gets the Egg access channel identifier.
    /// </summary>
    public const string EggChannel = "egg";

    /// <summary>
    /// Gets the machine access channel identifier.
    /// </summary>
    public const string MachineChannel = "machine";

    /// <summary>
    /// Gets the tutor access channel identifier.
    /// </summary>
    public const string TutorChannel = "tutor";

    /// <summary>
    /// Gets the player-side identifier.
    /// </summary>
    public const string PlayerSide = "player";

    /// <summary>
    /// Gets the enemy-side identifier.
    /// </summary>
    public const string EnemySide = "enemy";
}
