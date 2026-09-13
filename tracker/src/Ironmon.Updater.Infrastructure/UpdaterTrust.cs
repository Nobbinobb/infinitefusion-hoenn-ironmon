using Ironmon.Updater.Core;
namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Defines release trust independently of downloaded metadata and mutable installation files.
/// </summary>
public static class UpdaterTrust
{
    private const string ResourceName = "Ironmon.Updater.Infrastructure.TrustedKeys.json";

    /// <summary>
    /// Creates the production verifier with dedicated public keys embedded from reviewed source before signing.
    /// </summary>
    /// <remarks>
    /// Public trust is embedded at build time. Downloaded files and runtime environment variables cannot replace it.
    /// </remarks>
    /// <returns>A verifier that fails closed until production signing is provisioned.</returns>
    public static ReleaseVerifier CreateVerifier()
    {
        using var resource = typeof(UpdaterTrust).Assembly.GetManifestResourceStream(ResourceName) ?? throw new InvalidDataException(UpdaterText.UpdaterTrustEmbeddedUpdaterTrustIsMissing);
        using var bytes = new MemoryStream();
        resource.CopyTo(bytes);
        return new ReleaseVerifier(ReadKeys(bytes.ToArray()));
    }

    /// <summary>
    /// Parses dedicated public trust for the release tools with the same bounded contract used by installed clients.
    /// </summary>
    /// <param name="bytes">The exact public trust document from reviewed source.</param>
    /// <returns>The validated public key identifiers and SubjectPublicKeyInfo bytes.</returns>
    internal static IReadOnlyDictionary<string, byte[]> ReadKeys(byte[] bytes)
    {
        var values = ReleaseJson.Parse<Dictionary<string, string>>(bytes, ReleaseJson.SignatureLimit);
        if (values.Count > 4 || values.Keys.Any(key => key.Length is < 1 or > 64 || key.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-'))))
            throw new InvalidDataException(UpdaterText.UpdaterTrustUpdaterTrustContainsInvalidKeyIdentifiers);

        var keys = values.ToDictionary(pair => pair.Key, pair => Convert.FromBase64String(pair.Value), StringComparer.Ordinal);
        _ = new ReleaseVerifier(keys);
        return keys;
    }
}
