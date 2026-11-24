using System.Security.Cryptography;
using System.Net;
using Ironmon.ReleaseTool;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises the independently published signed worker contract without fixture signing keys in production.
/// </summary>
public sealed class StandaloneUpdaterBundleTests
{
    private const string KeyId = "fixture-worker";
    private const string WorkerVersion = "1.2.3";
    private const string NextWorkerVersion = "1.2.4";
    private const string Executable = "fixture.exe";
    private const string Candidate = "worker";
    private const string Trust = "trust.json";

    /// <summary>
    /// Verifies an independently signed worker and rejects a replacement binary or mismatched public tag.
    /// </summary>
    [Fact]
    public void SignedWorkerBindsExactBytesAndVersion()
    {
        using var workspace = new TestWorkspace();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var trust = workspace.PathFor(Trust);
        File.WriteAllBytes(trust, ReleaseJson.Serialize(new Dictionary<string, string> { [KeyId] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) }));
        var exe = workspace.PathFor(Executable);
        File.WriteAllBytes(exe, [1, 2, 3]);
        var candidate = workspace.PathFor(Candidate);
        StandaloneUpdaterBundle.Create(candidate, exe, WorkerVersion);
        var secret = key.ExportPkcs8PrivateKey();
        try
        {
            ReleaseSigning.SignWorker(candidate, trust, KeyId, secret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }

        var manifest = StandaloneUpdaterBundle.Verify(candidate, trust, true);
        var signature = File.ReadAllBytes(PlainPaths.Child(candidate, StandaloneUpdater.SignatureName));
        var verifier = new ReleaseVerifier(ReleaseBundle.ReadTrust(trust));
        var evidence = new ReleaseEvidence(manifest, signature, []);
        Assert.Equal(WorkerVersion, StandaloneUpdater.ReadVerified(verifier, evidence, WorkerVersion).Version);
        Assert.Throws<InvalidDataException>(() => StandaloneUpdater.ReadVerified(verifier, evidence, NextWorkerVersion));

        File.WriteAllBytes(PlainPaths.Child(candidate, StandaloneUpdater.ExecutableName), [3, 2, 1]);
        Assert.Throws<InvalidDataException>(() => StandaloneUpdaterBundle.Verify(candidate, trust, true));
    }

    /// <summary>
    /// Downloads two independently released worker versions for the same tracker target and rejects a replayed older worker.
    /// </summary>
    [Fact]
    public async Task NewWorkerCanLaunchTheSameReleaseWithoutChangingTheGameOrTracker()
    {
        using var workspace = new TestWorkspace();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = new ReleaseVerifier(new Dictionary<string, byte[]> { [KeyId] = key.ExportSubjectPublicKeyInfo() });
        byte[] first = [1, 2, 3];
        byte[] second = [4, 5, 6];
        var version = WorkerVersion;
        using var transport = new ReleaseHttpClient(new Handler(uri =>
        {
            if (uri.AbsoluteUri == ReleaseProtocol.ReleaseListUrl)
                return ReleaseJson.Serialize(new[] { new { tag_name = StandaloneUpdater.TagPrefix + version, draft = false, prerelease = true } });

            var binary = version == WorkerVersion ? first : second;
            var worker = new StandaloneUpdaterManifest(StandaloneUpdater.DocumentType, 1, ReleaseProtocol.Repository, version, binary.Length, TransactionStorage.Hash(binary));
            var manifest = ReleaseJson.Serialize(worker);
            return uri.Segments[^1] switch
            {
                StandaloneUpdater.ManifestName => manifest,
                StandaloneUpdater.SignatureName => SignDescriptor(key, manifest),
                StandaloneUpdater.ExecutableName => binary,
                _ => throw new InvalidOperationException()
            };
        }), TimeProvider.System);
        var updater = new StandaloneUpdater(transport, verifier, new ReleaseDownloadStore(workspace.PathFor(Candidate), transport));
        var target = new StandaloneUpdateTarget(workspace.Root, SignedReleaseFixture.VersionB, TransactionStorage.Hash([7]));
        var launched = new List<byte[]>();

        await updater.LaunchAsync(target, start => launched.Add(File.ReadAllBytes(start.FileName)), CancellationToken.None);
        version = NextWorkerVersion;
        await updater.LaunchAsync(target, start =>
        {
            Assert.Equal(target, StandaloneUpdateTarget.ReadArguments([.. start.ArgumentList]));
            launched.Add(File.ReadAllBytes(start.FileName));
        }, CancellationToken.None);
        version = WorkerVersion;
        await Assert.ThrowsAsync<InvalidDataException>(() => updater.LaunchAsync(target, _ => throw new InvalidOperationException(), CancellationToken.None));
        Assert.Equal(first, launched[0]);
        Assert.Equal(second, launched[1]);
    }

    /// <summary>
    /// Signs one exact descriptor with ephemeral test trust.
    /// </summary>
    /// <param name="key">The fixture's ephemeral signing key.</param>
    /// <param name="descriptor">The exact worker descriptor payload.</param>
    /// <returns>The detached signature document.</returns>
    private static byte[] SignDescriptor(ECDsa key, byte[] descriptor)
    {
        var signature = key.SignData(descriptor, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return ReleaseJson.Serialize(new ReleaseSignatures(ReleaseProtocol.SignatureDocument, 1, [new ReleaseSignature(KeyId, ReleaseProtocol.Algorithm, Convert.ToBase64String(signature))]));
    }

    /// <summary>
    /// Supplies worker discovery and downloads without calling GitHub.
    /// </summary>
    /// <remarks>
    /// Constructs a handler for the current fixture release.
    /// </remarks>
    /// <param name="read">The bounded response for the requested URI.</param>
    private sealed class Handler(Func<Uri, byte[]> read) : HttpMessageHandler
    {
        /// <summary>
        /// Returns the requested fixture bytes in an HTTP response.
        /// </summary>
        /// <param name="request">The actual request generated by the release client.</param>
        /// <param name="cancellationToken">The caller's cancellation token.</param>
        /// <returns>The fixture response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(read(request.RequestUri!)) });
    }
}
