using System.Security.Cryptography;

namespace Ironmon.Tracker.Access;

/// <summary>
/// Derives stable diagnostic signing-key identifiers from public key material.
/// </summary>
public static class DiagnosticAccessKeyIdentifiers
{
    /// <summary>
    /// Gets the stable prefix for P-256 signing-key identifiers.
    /// </summary>
    public const string P256Prefix = "p256-";

    /// <summary>
    /// Gets the number of SHA-256 fingerprint bytes represented in a key identifier.
    /// </summary>
    public const int FingerprintBytes = 16;

    /// <summary>
    /// Derives the stable key ID from DER SubjectPublicKeyInfo bytes.
    /// </summary>
    /// <param name="subjectPublicKeyInfo">The canonical public-key encoding.</param>
    /// <returns>The bounded lowercase P-256 key identifier.</returns>
    public static string DeriveP256(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        byte[] fingerprint = SHA256.HashData(subjectPublicKeyInfo);
        return P256Prefix + Convert.ToHexString(fingerprint.AsSpan(0, FingerprintBytes)).ToLowerInvariant();
    }
}
