namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Describes known individual values for one Pokemon instance.
/// </summary>
public sealed class IndividualValuesSnapshot
{
    /// <summary>
    /// Initializes unknown individual values for protocol serialization.
    /// </summary>
    public IndividualValuesSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the HP IV from zero through 31, or null when unknown.
    /// </summary>
    public int? Hp { get; init; }

    /// <summary>
    /// Gets or initializes the Attack IV from zero through 31, or null when unknown.
    /// </summary>
    public int? Attack { get; init; }

    /// <summary>
    /// Gets or initializes the Defense IV from zero through 31, or null when unknown.
    /// </summary>
    public int? Defense { get; init; }

    /// <summary>
    /// Gets or initializes the Special Attack IV from zero through 31, or null when unknown.
    /// </summary>
    public int? SpecialAttack { get; init; }

    /// <summary>
    /// Gets or initializes the Special Defense IV from zero through 31, or null when unknown.
    /// </summary>
    public int? SpecialDefense { get; init; }

    /// <summary>
    /// Gets or initializes the Speed IV from zero through 31, or null when unknown.
    /// </summary>
    public int? Speed { get; init; }
}
