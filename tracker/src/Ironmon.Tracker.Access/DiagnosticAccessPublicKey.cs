using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace Ironmon.Tracker.Access;

/// <summary>
/// Imports trusted P-256 public verification keys for diagnostic-access tokens.
/// </summary>
public static class DiagnosticAccessPublicKey
{
    private const string _publicKeyLabel = "PUBLIC KEY";

    /// <summary>
    /// Imports one PEM-encoded P-256 SubjectPublicKeyInfo key and assigns its deterministic key ID.
    /// </summary>
    /// <param name="pem">The public-key PEM text.</param>
    /// <returns>The ES256 verification key.</returns>
    /// <exception cref="ArgumentException">Thrown when the input is not one P-256 public key.</exception>
    public static ECDsaSecurityKey Import(string pem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pem);
        byte[] publicKey;
        try
        {
            PemFields fields = PemEncoding.Find(pem);
            if (!pem[fields.Label].SequenceEqual(_publicKeyLabel))
                throw new FormatException();

            publicKey = Convert.FromBase64String(pem[fields.Base64Data]);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new ArgumentException("The configured diagnostic verification key is not a valid P-256 public key.", nameof(pem), exception);
        }

        ECDsa key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(publicKey, out int bytesRead);
            if (bytesRead != publicKey.Length || key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
                throw new CryptographicException();

            string keyId = DiagnosticAccessKeyIdentifiers.DeriveP256(key.ExportSubjectPublicKeyInfo());
            return new ECDsaSecurityKey(key) { KeyId = keyId };
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            key.Dispose();
            throw new ArgumentException("The configured diagnostic verification key is not a valid P-256 public key.", nameof(pem), exception);
        }
    }
}
