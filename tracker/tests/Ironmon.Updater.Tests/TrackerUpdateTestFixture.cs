using System.Net;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises the production tracker session against isolated signed releases and a recorded shutdown boundary.
/// </summary>
internal sealed partial class TrackerUpdateTestFixture : ITrackerUpdateHost, IArtifactSource, IDisposable
{
    private const string TagPrefix = "v";
    private const string TagSegment = "tag/";
    private const string DiscoveryDirectory = "discovery";
    private const string GitDirectory = "git";
    private const string HomeDirectory = "home";
    private const string StagingDirectory = "staging";
    private const string ZipExtension = ".zip";
    private readonly TestWorkspace _workspace = new();
    private readonly ReleaseHttpClient _http;
    private readonly ReleaseDiscovery _discovery;

    /// <summary>
    /// Gets the same signed discovery service for disposable Setup session fixtures.
    /// </summary>
    internal ReleaseDiscovery Discovery => _discovery;

    /// <summary>
    /// Gets the real independently signed disposable release.
    /// </summary>
    internal SignedReleaseFixture Release { get; }

    /// <summary>
    /// Gets the production metadata-first preparation workflow.
    /// </summary>
    internal TrackerUpdatePreparation Preparation { get; }

    /// <summary>
    /// Gets the production session connected to the recorded host.
    /// </summary>
    internal TrackerUpdateSession Session { get; }

    /// <summary>
    /// Gets or sets the latest conservative run observation.
    /// </summary>
    internal bool ActiveRun { get; set; }

    /// <summary>
    /// Gets or sets the running tracker version represented by the fixture.
    /// </summary>
    internal string ObservedVersion { get; set; } = SignedReleaseFixture.VersionA;

    /// <summary>
    /// Gets or sets a different local game revision for display verification.
    /// </summary>
    internal string? ObservedGameCommit { get; set; }

    /// <summary>
    /// Gets the number of release metadata transport requests.
    /// </summary>
    internal int MetadataRequests { get; private set; }

    /// <summary>
    /// Gets or sets a cancellable metadata pause for overlapping check tests.
    /// </summary>
    internal TaskCompletionSource? MetadataGate { get; set; }

    /// <summary>
    /// Gets the signal that discovery reached its transport boundary.
    /// </summary>
    internal TaskCompletionSource MetadataStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets or sets simulated public release transport failure.
    /// </summary>
    internal bool Offline { get; set; }

    /// <summary>
    /// Gets or sets the callback run before a program archive is supplied.
    /// </summary>
    internal Action? DuringArchive { get; set; }

    /// <summary>
    /// Gets or sets an optional cancellable archive pause for interaction tests.
    /// </summary>
    internal TaskCompletionSource? ArchiveGate { get; set; }

    /// <summary>
    /// Gets the notification that a large download boundary has been reached.
    /// </summary>
    internal TaskCompletionSource ArchiveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets or sets simulated helper acknowledgement failure.
    /// </summary>
    internal bool FailHandoff { get; set; }

    /// <summary>
    /// Gets the exact prepared transaction passed to the lifecycle boundary.
    /// </summary>
    internal PreparedIronmonUpdate? Handoff { get; private set; }

    /// <summary>
    /// Gets whether the lifecycle boundary was asked for recovery.
    /// </summary>
    internal bool RecoveryHandoff { get; private set; }

    /// <summary>
    /// Constructs real services with transport and process effects confined to the test fixture.
    /// </summary>
    /// <param name="flavor">The installed package represented by the signed fixture.</param>
    internal TrackerUpdateTestFixture(string flavor = ReleaseProtocol.RuntimeRequired)
    {
        Release = new SignedReleaseFixture(flavor);
        _http = new ReleaseHttpClient(new Handler(this), TimeProvider.System);
        _discovery = new ReleaseDiscovery(_http, Release.Verifier, _workspace.PathFor(DiscoveryDirectory));
        var downloads = new ReleaseDownloadStore(Release.Cache, this);
        var cache = new MinGitCache(_workspace.PathFor(GitDirectory), this);
        var policy = new RepositoryPolicy(new Uri(ReleaseProtocol.GameRepository), ReleaseProtocol.GameBranch);
        var game = new CombinedGamePreparation(cache, policy, _workspace.PathFor(StagingDirectory));
        var verification = new CombinedGitVerification(cache, policy, _workspace.PathFor(HomeDirectory));
        Preparation = new TrackerUpdatePreparation(Release.Verifier, downloads, SignedReleaseFixture.Runtime(), game, verification);
        Session = new TrackerUpdateSession(_discovery, Release.Verifier, Preparation, downloads, this);
    }

    /// <summary>
    /// Returns the disposable installation and fresh run observation.
    /// </summary>
    /// <returns>The fixture selection.</returns>
    public IronmonUpdateRequest Observe()
        => Release.Request() with { HasActiveRun = ActiveRun, CurrentVersion = ObservedVersion, GameCommit = ObservedGameCommit ?? Release.Request().GameCommit };

    /// <summary>
    /// Records handoff without ever closing a real tracker or game process.
    /// </summary>
    /// <param name="prepared">The real prepared transaction.</param>
    /// <param name="recover">The selected operation.</param>
    /// <param name="cancellationToken">The pre-shutdown token.</param>
    /// <returns>The recorded acknowledgement or injected failure.</returns>
    public Task HandoffAsync(PreparedIronmonUpdate prepared, bool recover, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailHandoff)
            throw new IOException("The independent updater could not acknowledge readiness.");

        Handoff = prepared;
        RecoveryHandoff = recover;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Supplies only fixture assets while allowing deterministic interruption during large downloads.
    /// </summary>
    /// <param name="source">The signed asset URL.</param>
    /// <param name="destination">The production cache output.</param>
    /// <param name="maximumBytes">The signed size bound.</param>
    /// <param name="cancellationToken">The active download token.</param>
    /// <returns>The actual fixture asset stream.</returns>
    public async Task CopyToAsync(Uri source, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        if (source.AbsolutePath.EndsWith(ZipExtension, StringComparison.Ordinal))
        {
            ArchiveStarted.TrySetResult();
            DuringArchive?.Invoke();
            if (ArchiveGate is { } gate)
                await gate.Task.WaitAsync(cancellationToken);
        }

        await Release.CopyToAsync(source, destination, maximumBytes, cancellationToken);
    }

    /// <summary>
    /// Frees session resources before removing the disposable installation and private key.
    /// </summary>
    public void Dispose()
    {
        Session.Dispose();
        _discovery.Dispose();
        _http.Dispose();
        Release.Dispose();
        _workspace.Dispose();
    }

    /// <summary>
    /// Supplies signed discovery documents through the production HTTP policy.
    /// </summary>
    /// <remarks>
    /// Constructs the isolated transport handler, never opening a network connection.
    /// </remarks>
    /// <param name="fixture">The owning signed session fixture.</param>
    private sealed partial class Handler(TrackerUpdateTestFixture fixture) : HttpMessageHandler
    {
        private readonly TrackerUpdateTestFixture _fixture = fixture;

        /// <summary>
        /// Returns only the fixture's public API, manifest or signature response.
        /// </summary>
        /// <param name="request">The production metadata request.</param>
        /// <param name="cancellationToken">The request token.</param>
        /// <returns>The bounded response.</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _fixture.MetadataRequests++;
            _fixture.MetadataStarted.TrySetResult();
            if (_fixture.MetadataGate is { } gate)
                await gate.Task.WaitAsync(cancellationToken);

            if (_fixture.Offline)
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var release = _fixture.Release;
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);
            var api = new { tag_name = TagPrefix + release.Manifest.IronmonVersion, draft = false, prerelease = false, html_url = ReleaseProtocol.ReleaseBaseUrl + TagSegment + TagPrefix + release.Manifest.IronmonVersion, assets = new[] { ReleaseProtocol.ManifestName, ReleaseProtocol.SignatureName }.Select(name => new { name, browser_download_url = ReleaseProtocol.AssetUrl(release.Manifest.IronmonVersion, name) }) };
            var bytes = name switch
            {
                ReleaseProtocol.ManifestName => release.Evidence.Manifest,
                ReleaseProtocol.SignatureName => release.Evidence.Signatures,
                _ => ReleaseJson.Serialize(api)
            };

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }
    }
}
