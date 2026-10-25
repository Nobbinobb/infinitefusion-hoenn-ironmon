namespace Ironmon.Tracker.Access;

/// <summary>
/// Identifies the outcome of diagnostic-access token validation.
/// </summary>
public enum DiagnosticAccessValidationStatus
{
    /// <summary>
    /// The token is valid and may grant its supported capabilities.
    /// </summary>
    Valid = 0,

    /// <summary>The input is not a bounded JWS Compact Serialization token.
    /// </summary>
    Malformed = 1,

    /// <summary>
    /// The protected JWT algorithm is unsupported.
    /// </summary>
    UnsupportedAlgorithm = 2,

    /// <summary>
    /// The protected JWT type is unsupported.
    /// </summary>
    UnsupportedType = 3,

    /// <summary>
    /// The protected JWT key ID is not trusted.
    /// </summary>
    UnknownKey = 4,

    /// <summary>
    /// The token signature is invalid.
    /// </summary>
    InvalidSignature = 5,

    /// <summary>
    /// The signed JWT claims violate the Ironmon access contract.
    /// </summary>
    InvalidClaims = 6,

    /// <summary>
    /// The signed token has reached its expiration instant.
    /// </summary>
    Expired = 7
}
