namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Describes one authored trainer slot which generates a lookup species.
/// </summary>
public sealed class TrainerPokemonOccurrenceSnapshot
{
    /// <summary>
    /// Initializes an empty trainer occurrence for protocol serialization.
    /// </summary>
    public TrainerPokemonOccurrenceSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the stable trainer-data identifier.
    /// </summary>
    public required string TrainerId { get; init; }

    /// <summary>
    /// Gets or initializes the localized trainer name.
    /// </summary>
    public required string TrainerName { get; init; }

    /// <summary>
    /// Gets or initializes the localized trainer type.
    /// </summary>
    public required string TrainerType { get; init; }

    /// <summary>
    /// Gets or initializes the one-based party slot.
    /// </summary>
    public int Slot { get; init; }

    /// <summary>
    /// Gets or initializes the authored party level.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Gets or initializes the map identifier when a trainer event was found.
    /// </summary>
    public int? MapId { get; init; }

    /// <summary>
    /// Gets or initializes the localized trainer route or map name when known.
    /// </summary>
    public string? RouteName { get; init; }

    /// <summary>
    /// Gets or initializes the stable source-species identifier.
    /// </summary>
    public required string SourceSpeciesId { get; init; }

    /// <summary>
    /// Gets or initializes the localized source-species name.
    /// </summary>
    public required string SourceSpeciesName { get; init; }
}
