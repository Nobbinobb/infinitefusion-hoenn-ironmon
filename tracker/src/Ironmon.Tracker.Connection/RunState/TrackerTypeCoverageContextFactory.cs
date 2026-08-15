namespace Ironmon.Tracker.Connection.RunState;

/// <summary>
/// Converts optional protocol coverage metadata into the tracker coverage domain.
/// </summary>
public static class TrackerTypeCoverageContextFactory
{
    /// <summary>
    /// Creates a connected coverage context when the game supplies the optional metadata.
    /// </summary>
    /// <param name="game">The connected game handshake.</param>
    /// <param name="state">The recovered current state.</param>
    /// <returns>The connected context, or null for older games without coverage metadata.</returns>
    public static TypeCoverageContext? Create(GameHandshakePayload? game, GameCurrentStatePayload? state)
    {
        TypeCoverageContextPayload? payload = state?.TypeCoverage;
        if (game is null || payload is null)
            return null;

        return new(
            game.GameVersion,
            ParsePolicy(payload.TrainerPolicy),
            payload.NormalPoolSize,
            payload.NormalPoolFingerprint ?? string.Empty,
            payload.FusionPoolSchemaVersion,
            payload.FusionPoolSize,
            payload.FusionPoolFingerprint ?? string.Empty);
    }

    /// <summary>
    /// Converts one protocol trainer-policy identifier into the coverage domain.
    /// </summary>
    /// <param name="policy">The protocol policy identifier.</param>
    /// <returns>The recognized coverage policy, or Unknown.</returns>
    private static TypeCoveragePolicy ParsePolicy(string? policy) => policy switch
    {
        "mixed" => TypeCoveragePolicy.Mixed,
        "custom_fusions_only" => TypeCoveragePolicy.CustomFusionsOnly,
        "normal_only" => TypeCoveragePolicy.NormalOnly,
        _ => TypeCoveragePolicy.Unknown
    };
}
