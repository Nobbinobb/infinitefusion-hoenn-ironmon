using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace Ironmon.Tracker.AccessGenerator;

/// <summary>
/// Owns one externally loaded P-256 private key for the generator process lifetime.
/// </summary>
public sealed class DiagnosticAccessSigningKey : IDisposable
{
    private const string PrivateKeyLabel = "PRIVATE KEY";
    private readonly ECDsa _key;

    /// <summary>
    /// Initializes an owned signing key.
    /// </summary>
    /// <param name="key">The imported P-256 private key.</param>
    /// <param name="sourceName">The non-sensitive source filename.</param>
    private DiagnosticAccessSigningKey(ECDsa key, string sourceName)
    {
        _key = key;
        SourceName = sourceName;
        KeyId = DiagnosticAccessKeyIdentifiers.DeriveP256(key.ExportSubjectPublicKeyInfo());
    }

    /// <summary>
    /// Gets the deterministic public-key fingerprint used as the JWT key ID.
    /// </summary>
    public string KeyId { get; }

    /// <summary>
    /// Gets the non-sensitive selected source filename.
    /// </summary>
    public string SourceName { get; }

    /// <summary>
    /// Imports an external PEM-encoded PKCS#8 P-256 private key.
    /// </summary>
    /// <param name="pem">The PEM text read from the selected file.</param>
    /// <param name="sourceName">The non-sensitive selected filename.</param>
    /// <returns>The owned signing key.</returns>
    /// <exception cref="ArgumentException">Thrown when input is empty or does not contain a P-256 private key.</exception>
    public static DiagnosticAccessSigningKey Import(string pem, string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pem);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (pem.Length > DiagnosticAccessGeneratorConstants.MaximumPrivateKeyCharacters)
            throw new ArgumentException("The selected private-key file is too large.", nameof(pem));

        byte[] privateKey;
        try
        {
            PemFields fields = PemEncoding.Find(pem);
            if (!pem[fields.Label].SequenceEqual(PrivateKeyLabel))
                throw new FormatException();

            privateKey = Convert.FromBase64String(pem[fields.Base64Data]);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new ArgumentException("The selected file does not contain a valid PKCS#8 P-256 private key.", nameof(pem), exception);
        }

        ECDsa key = ECDsa.Create();
        try
        {
            key.ImportPkcs8PrivateKey(privateKey, out int bytesRead);
            if (bytesRead != privateKey.Length)
                throw new CryptographicException("The private-key payload contains trailing data.");

            if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
                throw new ArgumentException("The selected private key must use the P-256 curve.", nameof(pem));

            key.SignData("Ironmon diagnostic access"u8, HashAlgorithmName.SHA256);
            return new DiagnosticAccessSigningKey(key, Path.GetFileName(sourceName));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            key.Dispose();
            throw new ArgumentException("The selected file does not contain a valid PKCS#8 P-256 private key.", nameof(pem), exception);
        }
        catch
        {
            key.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    /// <summary>
    /// Creates signing credentials backed by the owned private key.
    /// </summary>
    /// <returns>The ES256 signing credentials.</returns>
    internal SigningCredentials CreateSigningCredentials()
        => new(new ECDsaSecurityKey(_key) { KeyId = KeyId }, SecurityAlgorithms.EcdsaSha256);

    /// <summary>
    /// Exports the public key parameters for verification and public-key registration.
    /// </summary>
    /// <returns>The public P-256 parameters.</returns>
    public ECParameters ExportPublicParameters()
        => _key.ExportParameters(false);

    /// <summary>
    /// Releases private-key material owned by the generator.
    /// </summary>
    public void Dispose()
    {
        _key.Dispose();
        GC.SuppressFinalize(this);
    }

}
