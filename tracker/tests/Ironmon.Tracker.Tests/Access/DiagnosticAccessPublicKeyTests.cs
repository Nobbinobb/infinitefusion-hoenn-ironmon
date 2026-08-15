using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace Ironmon.Tracker.Tests.Access;

/// <summary>
/// Verifies strict trusted public-key import and deterministic key identities.
/// </summary>
public sealed class DiagnosticAccessPublicKeyTests
{
    /// <summary>
    /// Initializes the public-key import tests.
    /// </summary>
    public DiagnosticAccessPublicKeyTests()
    {
    }

    /// <summary>
    /// Verifies a matching private and public key derive the same token key ID.
    /// </summary>
    [Fact]
    public void MatchingPrivateAndPublicKeysDeriveSameKeyId()
    {
        using ECDsa source = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using DiagnosticAccessSigningKey privateKey = DiagnosticAccessSigningKey.Import(source.ExportPkcs8PrivateKeyPem(), "private.pem");
        ECDsaSecurityKey publicKey = DiagnosticAccessPublicKey.Import(source.ExportSubjectPublicKeyInfoPem());
        using ECDsa ownedPublicKey = publicKey.ECDsa;

        Assert.Equal(privateKey.KeyId, publicKey.KeyId);
        Assert.StartsWith(DiagnosticAccessKeyIdentifiers.P256Prefix, publicKey.KeyId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a private-key PEM cannot be registered as a trusted public key.
    /// </summary>
    [Fact]
    public void PrivateKeyPemIsRejected()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Throws<ArgumentException>(() => DiagnosticAccessPublicKey.Import(key.ExportPkcs8PrivateKeyPem()));
    }

    /// <summary>
    /// Verifies a different elliptic curve is rejected even when its PEM structure is valid.
    /// </summary>
    [Fact]
    public void NonP256PublicKeyIsRejected()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        Assert.Throws<ArgumentException>(() => DiagnosticAccessPublicKey.Import(key.ExportSubjectPublicKeyInfoPem()));
    }
}
