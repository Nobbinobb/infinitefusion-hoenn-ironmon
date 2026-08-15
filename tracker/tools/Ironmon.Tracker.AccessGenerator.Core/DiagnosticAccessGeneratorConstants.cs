namespace Ironmon.Tracker.AccessGenerator;

/// <summary>
/// Defines stable limits and file conventions for the diagnostic-access generator.
/// </summary>
public static class DiagnosticAccessGeneratorConstants
{
    /// <summary>
    /// Gets the generated token-file extension.
    /// </summary>
    public const string TokenFileExtension = ".ironmon-access";

    /// <summary>
    /// Gets the maximum accepted private-key PEM length.
    /// </summary>
    public const int MaximumPrivateKeyCharacters = 32_768;

    /// <summary>
    /// Gets the stable prefix for derived P-256 signing-key identifiers.
    /// </summary>
    public const string KeyIdPrefix = DiagnosticAccessKeyIdentifiers.P256Prefix;

    /// <summary>
    /// Gets the number of SHA-256 fingerprint bytes retained in a derived key ID.
    /// </summary>
    public const int KeyIdFingerprintBytes = DiagnosticAccessKeyIdentifiers.FingerprintBytes;
}
