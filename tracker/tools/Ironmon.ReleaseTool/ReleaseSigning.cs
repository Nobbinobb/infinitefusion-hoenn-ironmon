using System.Security.Cryptography;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.ReleaseTool;

/// <summary>
/// Signs only already verified immutable manifest bytes in the protected publication process.
/// </summary>
internal static class ReleaseSigning
{
    internal const string KeyVariable = "IRONMON_UPDATER_SIGNING_KEY";
    internal const string IdVariable = "IRONMON_UPDATER_SIGNING_KEY_ID";

    /// <summary>
    /// Adds a detached signature after checking the private key against independently reviewed public trust.
    /// </summary>
    /// <param name="directory">The verified frozen candidate.</param>
    /// <param name="trustFile">Public trust from trusted publication source.</param>
    /// <param name="keyId">The dedicated updater key identifier.</param>
    /// <param name="privateKey">The ephemeral P-256 PKCS8 private bytes, never written to an artifact.</param>
    internal static void Sign(string directory, string trustFile, string keyId, ReadOnlySpan<byte> privateKey)
    {
        ReleaseBundle.Verify(directory, trustFile, false);
        var manifest = File.ReadAllBytes(PlainPaths.Child(directory, ReleaseProtocol.ManifestName));
        WriteSignature(directory, trustFile, keyId, privateKey, manifest, ReleaseProtocol.SignatureName);
        ReleaseBundle.Verify(directory, trustFile, true);
    }

    /// <summary>
    /// Signs the independent worker candidate using the same reviewed public trust as tracker releases.
    /// </summary>
    /// <param name="directory">The frozen worker candidate.</param>
    /// <param name="trustFile">The reviewed public signing trust.</param>
    /// <param name="keyId">The dedicated updater signing key identifier.</param>
    /// <param name="privateKey">The ephemeral PKCS8 signing key.</param>
    internal static void SignWorker(string directory, string trustFile, string keyId, ReadOnlySpan<byte> privateKey)
    {
        var manifest = StandaloneUpdaterBundle.Verify(directory, trustFile, false);
        WriteSignature(directory, trustFile, keyId, privateKey, manifest, StandaloneUpdater.SignatureName);
        StandaloneUpdaterBundle.Verify(directory, trustFile, true);
    }

    /// <summary>
    /// Writes a detached signature for exact verified bytes, retaining an existing signature for retries.
    /// </summary>
    /// <param name="directory">The frozen artifact directory.</param>
    /// <param name="trustFile">The independently reviewed public signing trust.</param>
    /// <param name="keyId">The dedicated key identifier.</param>
    /// <param name="privateKey">The ephemeral PKCS8 private key.</param>
    /// <param name="manifest">The verified exact descriptor bytes.</param>
    /// <param name="signatureName">The fixed detached signature basename.</param>
    private static void WriteSignature(string directory, string trustFile, string keyId, ReadOnlySpan<byte> privateKey, byte[] manifest, string signatureName)
    {
        var keys = ReleaseBundle.ReadTrust(trustFile);
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(privateKey, out var consumed);
        if (consumed != privateKey.Length || !keys.TryGetValue(keyId, out var expected) || !key.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(expected))
            throw new InvalidDataException("The signing key does not match the reviewed updater public trust.");

        var signaturePath = PlainPaths.Child(directory, signatureName);
        if (File.Exists(signaturePath))
            return;

        var signature = key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var document = new ReleaseSignatures(ReleaseProtocol.SignatureDocument, 1, [new ReleaseSignature(keyId, ReleaseProtocol.Algorithm, Convert.ToBase64String(signature))]);
        ReleaseBundle.WriteImmutable(directory, signatureName, ReleaseJson.Serialize(document));
    }
}
