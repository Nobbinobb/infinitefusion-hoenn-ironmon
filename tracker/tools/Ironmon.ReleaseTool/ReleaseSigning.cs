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
        var keys = ReleaseBundle.ReadTrust(trustFile);
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(privateKey, out var consumed);
        if (consumed != privateKey.Length || !keys.TryGetValue(keyId, out var expected) || !key.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(expected))
            throw new InvalidDataException("The signing key does not match the reviewed updater public trust.");

        var signaturePath = PlainPaths.Child(directory, ReleaseProtocol.SignatureName);
        if (File.Exists(signaturePath))
        {
            ReleaseBundle.Verify(directory, trustFile, true);
            return;
        }

        var manifest = File.ReadAllBytes(PlainPaths.Child(directory, ReleaseProtocol.ManifestName));
        var signature = key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var document = new ReleaseSignatures(ReleaseProtocol.SignatureDocument, 1, [new ReleaseSignature(keyId, ReleaseProtocol.Algorithm, Convert.ToBase64String(signature))]);
        ReleaseBundle.WriteImmutable(directory, ReleaseProtocol.SignatureName, ReleaseJson.Serialize(document));
        ReleaseBundle.Verify(directory, trustFile, true);
    }
}
