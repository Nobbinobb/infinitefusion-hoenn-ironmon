namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Defines stable error codes returned by tracker request handlers.
/// </summary>
public static class TrackerErrorCodes
{
    /// <summary>
    /// Gets the error code returned when a deterministic request or result is malformed.
    /// </summary>
    public const string InvalidQuery = "invalid_query";

    /// <summary>
    /// Gets the error code returned when a requested Pokemon is no longer available.
    /// </summary>
    public const string PokemonNotFound = "pokemon_not_found";
}
