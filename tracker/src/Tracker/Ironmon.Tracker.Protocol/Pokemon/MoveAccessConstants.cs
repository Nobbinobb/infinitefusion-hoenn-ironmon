namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Defines stable move-access source identifiers and sentinel values.
/// </summary>
public static class MoveAccessConstants
{
    /// <summary>
    /// Gets the learned-level sentinel used for evolution moves.
    /// </summary>
    public const int EvolutionLearnedLevel = 0;

    /// <summary>
    /// Gets the level-up source identifier.
    /// </summary>
    public const string LevelUpSource = "level_up";

    /// <summary>
    /// Gets the Egg-move source identifier.
    /// </summary>
    public const string EggSource = "egg";

    /// <summary>
    /// Gets the TM source identifier.
    /// </summary>
    public const string TmSource = "tm";

    /// <summary>
    /// Gets the TR source identifier.
    /// </summary>
    public const string TrSource = "tr";

    /// <summary>
    /// Gets the ordinary-tutor source identifier.
    /// </summary>
    public const string OrdinaryTutorSource = "ordinary_tutor";

    /// <summary>
    /// Gets the regular Fusion Tutor source identifier.
    /// </summary>
    public const string FusionTutorRegularSource = "fusion_tutor_regular";

    /// <summary>
    /// Gets the legendary Fusion Tutor source identifier.
    /// </summary>
    public const string FusionTutorLegendarySource = "fusion_tutor_legendary";
}
