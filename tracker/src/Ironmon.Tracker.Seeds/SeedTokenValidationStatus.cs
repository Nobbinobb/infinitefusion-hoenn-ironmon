namespace Ironmon.Tracker.Seeds;

/// <summary>
/// Identifies one seeded-run token validation outcome.
/// </summary>
public enum SeedTokenValidationStatus
{
    /// <summary>
    /// The token is valid.
    /// </summary>
    Valid = 0,

    /// <summary>
    /// The input is not a bounded JWS Compact Serialization token.
    /// </summary>
    Malformed = 1,

    /// <summary>
    /// The protected signing algorithm is unsupported.
    /// </summary>
    UnsupportedAlgorithm = 2,

    /// <summary>
    /// The protected token type is not a seeded-run token.
    /// </summary>
    UnsupportedType = 3,

    /// <summary>
    /// The protected key identity is unsupported.
    /// </summary>
    UnknownKey = 4,

    /// <summary>
    /// The token signature is invalid.
    /// </summary>
    InvalidSignature = 5,

    /// <summary>
    /// The signed claims violate the seeded-run contract.
    /// </summary>
    InvalidClaims = 6
}
