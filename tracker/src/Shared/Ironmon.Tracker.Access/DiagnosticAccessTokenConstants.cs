namespace Ironmon.Tracker.Access;

/// <summary>
/// Defines the fixed application contract for Ironmon diagnostic-access JWTs.
/// </summary>
public static class DiagnosticAccessTokenConstants
{
    /// <summary>
    /// Gets the accepted JWT type.
    /// </summary>
    public const string TokenType = "ironmon-access+jwt";

    /// <summary>
    /// Gets the accepted JWT issuer.
    /// </summary>
    public const string Issuer = "ironmon";

    /// <summary>
    /// Gets the accepted JWT audience.
    /// </summary>
    public const string Audience = "ironmon-tracker";

    /// <summary>
    /// Gets the diagnostic-access application contract version.
    /// </summary>
    public const int ContractVersion = 1;

    /// <summary>
    /// Gets the custom application-version claim name.
    /// </summary>
    public const string VersionClaim = "ver";

    /// <summary>
    /// Gets the optional support-note claim name.
    /// </summary>
    public const string NoteClaim = "note";

    /// <summary>
    /// Gets the capability-list claim name.
    /// </summary>
    public const string CapabilitiesClaim = "capabilities";

    /// <summary>
    /// Gets the maximum accepted compact-token length.
    /// </summary>
    public const int MaximumTokenLength = 32_768;

    /// <summary>
    /// Gets the maximum accepted token-ID length.
    /// </summary>
    public const int MaximumTokenIdLength = 128;

    /// <summary>
    /// Gets the maximum accepted key-ID length.
    /// </summary>
    public const int MaximumKeyIdLength = 128;

    /// <summary>
    /// Gets the maximum accepted support-note length.
    /// </summary>
    public const int MaximumNoteLength = 256;

    /// <summary>
    /// Gets the maximum number of capability IDs accepted from one token.
    /// </summary>
    public const int MaximumCapabilityCount = 128;

    /// <summary>
    /// Gets the maximum accepted capability-ID length.
    /// </summary>
    public const int MaximumCapabilityIdLength = 128;
}
