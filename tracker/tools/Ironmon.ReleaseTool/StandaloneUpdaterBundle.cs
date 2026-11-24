using Ironmon.Updater.Infrastructure;

namespace Ironmon.ReleaseTool;

/// <summary>
/// Creates and validates a worker release from an independently built Setup executable.
/// </summary>
internal static class StandaloneUpdaterBundle
{
    /// <summary>
    /// Freezes the executable and versioned bootstrap metadata in a new local candidate.
    /// </summary>
    /// <param name="directory">The dedicated worker candidate directory.</param>
    /// <param name="executable">The self-contained Setup executable built from reviewed source.</param>
    /// <param name="version">The independently assigned worker version.</param>
    internal static void Create(string directory, string executable, string version)
    {
        directory = PlainPaths.Full(directory);
        var content = TransactionStorage.ContentAsync(PlainPaths.Full(executable), CancellationToken.None).GetAwaiter().GetResult();
        var manifest = new StandaloneUpdaterManifest(StandaloneUpdater.DocumentType, 1, ReleaseProtocol.Repository, version, content.Length, content.Sha256);
        StandaloneUpdater.Validate(manifest);
        Directory.CreateDirectory(directory);
        var destination = PlainPaths.Child(directory, StandaloneUpdater.ExecutableName);
        TransactionStorage.CopyAsync(executable, destination, content, CancellationToken.None).GetAwaiter().GetResult();
        ReleaseBundle.WriteImmutable(directory, StandaloneUpdater.ManifestName, ReleaseJson.Serialize(manifest));
    }

    /// <summary>
    /// Rechecks the candidate binary against its precise manifest and optionally validates the production signature.
    /// </summary>
    /// <param name="directory">The frozen worker candidate.</param>
    /// <param name="trustFile">Public updater trust from reviewed source.</param>
    /// <param name="signed">Whether a detached trusted signature is required.</param>
    /// <returns>The exact manifest bytes to sign or retain.</returns>
    internal static byte[] Verify(string directory, string trustFile, bool signed)
    {
        var bytes = File.ReadAllBytes(PlainPaths.Child(directory, StandaloneUpdater.ManifestName));
        var manifest = ReleaseJson.Parse<StandaloneUpdaterManifest>(bytes, ReleaseJson.SignatureLimit);
        StandaloneUpdater.Validate(manifest);
        if (signed)
        {
            var signature = File.ReadAllBytes(PlainPaths.Child(directory, StandaloneUpdater.SignatureName));
            var verifier = new ReleaseVerifier(ReleaseBundle.ReadTrust(trustFile));
            StandaloneUpdater.ReadVerified(verifier, new ReleaseEvidence(bytes, signature, []), manifest.Version);
        }

        var executable = PlainPaths.Child(directory, StandaloneUpdater.ExecutableName);
        if (TransactionStorage.ContentAsync(executable, CancellationToken.None).GetAwaiter().GetResult() != ReleaseProtocol.Content(manifest.Bytes, manifest.Sha256))
            throw new InvalidDataException("The standalone updater executable differs from the candidate manifest.");

        return bytes;
    }
}
