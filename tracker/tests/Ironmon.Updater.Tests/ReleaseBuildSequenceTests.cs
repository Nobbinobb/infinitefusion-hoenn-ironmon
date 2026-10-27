using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Ironmon.ReleaseTool;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises the production sequence allocator against independently signed releases from different version lines.
/// </summary>
public sealed class ReleaseBuildSequenceTests
{
    private const string SequenceScript = "tools/ci/Get-ReleaseBuildSequence.ps1";
    private const string ArtifactScript = "tools/ci/UpdateArtifacts.ps1";
    private const string StagedSource = "source";
    private const string TrustPath = "resources/updater/trusted-keys.json";
    private const string DriverName = "allocate.ps1";
    private const string ReleasesName = "releases.json";
    private const string ReleaseRootName = "releases";
    private const string ContainerName = "update-data.json";
    private const string PowerShell = "pwsh";
    private const string NoProfileArgument = "-NoProfile";
    private const string FileArgument = "-File";
    private const string ScriptArgument = "-SequenceScript";
    private const string ToolArgument = "-Tool";
    private const string ListArgument = "-ReleaseList";
    private const string RootArgument = "-ReleaseRoot";
    private const string TargetVersion = "0.10.0";
    private const string BridgeVersion = "0.8.16";
    private const string TagPrefix = "v";
    private const string DigestPrefix = "sha256:";
    private const string InvalidDigest = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
    private const string Driver = """
        param([string]$SequenceScript, [string]$Tool, [string]$ReleaseList, [string]$ReleaseRoot)
        $ErrorActionPreference = 'Stop'
        function gh {
          $arguments = $args
          if ($arguments[0] -ceq 'api') {
            Get-Content -LiteralPath $ReleaseList -Raw
            return
          }
          if ($arguments[0] -ceq 'release' -and $arguments[1] -ceq 'download') {
            $name = $arguments[[Array]::IndexOf($arguments, '--pattern') + 1]
            $directory = $arguments[[Array]::IndexOf($arguments, '--dir') + 1]
            Copy-Item -LiteralPath (Join-Path (Join-Path $ReleaseRoot $arguments[2]) $name) -Destination (Join-Path $directory $name)
            return
          }
          throw 'Unexpected command at the isolated GitHub boundary.'
        }
        $env:GITHUB_ACTIONS = 'true'
        & $SequenceScript -Version '0.10.0' -Tool $Tool
        """;

    /// <summary>
    /// Allocates above a signed bridge even when its display version is lower than the target release's display version.
    /// </summary>
    /// <param name="draft">Whether the higher-sequence bridge is temporarily unpublished.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllocatesAboveOtherVersionLinesAndVisibleSignedDrafts(bool draft)
    {
        using var fixture = new ReleaseBundleFixture();
        fixture.Build(TargetVersion, releaseSequence: 15);
        fixture.Sign(TargetVersion);
        fixture.Build(BridgeVersion, releaseSequence: 19);
        fixture.Sign(BridgeVersion);
        var result = await AllocateAsync(fixture, [Release(fixture, TargetVersion), Release(fixture, BridgeVersion, draft)]);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(20, int.Parse(result.Output.Trim(), CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Retains issued identities when temporary releases have been removed or are hidden from a read-only build token.
    /// </summary>
    [Fact]
    public async Task MissingTemporaryHistoryRetainsTheReservedFloor()
    {
        using var fixture = new ReleaseBundleFixture();
        var result = await AllocateAsync(fixture, []);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(16, int.Parse(result.Output.Trim(), CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Refuses allocation when historical bytes fail independent asset digests or release-signature verification.
    /// </summary>
    /// <param name="invalidSignature">Whether to provide digest-consistent bytes with an invalid signature instead of an incorrect digest.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnverifiableHistoryCannotSilentlyResetTheSequence(bool invalidSignature)
    {
        using var fixture = new ReleaseBundleFixture();
        fixture.Build(BridgeVersion, releaseSequence: 19);
        fixture.Sign(BridgeVersion);
        if (invalidSignature)
            File.WriteAllBytes(Path.Combine(fixture.DirectoryFor(BridgeVersion), ReleaseProtocol.SignatureName), [0]);

        var result = await AllocateAsync(fixture, [Release(fixture, BridgeVersion, invalidDigest: !invalidSignature)]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.NotEmpty(result.Error);
        Assert.DoesNotContain(20.ToString(CultureInfo.InvariantCulture), result.Output.Trim());
    }

    /// <summary>
    /// Runs the unmodified production scripts and validator in an owned fixture with only GitHub transport replaced.
    /// </summary>
    /// <param name="fixture">The independent signing trust and actual producer output.</param>
    /// <param name="releases">The simulated public or maintainer-visible release-list entries.</param>
    /// <returns>The allocator process exit code and captured output streams.</returns>
    private static async Task<(int ExitCode, string Output, string Error)> AllocateAsync(ReleaseBundleFixture fixture, object[] releases)
    {
        using var workspace = new TestWorkspace();
        var source = workspace.PathFor(StagedSource);
        var repository = FindRepository();
        foreach (var relative in new[] { SequenceScript, ArtifactScript })
        {
            var destination = Path.Combine(source, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(repository, relative), destination);
        }

        var trust = Path.Combine(source, TrustPath);
        Directory.CreateDirectory(Path.GetDirectoryName(trust)!);
        File.Copy(fixture.Trust, trust);
        var releaseRoot = workspace.PathFor(ReleaseRootName);
        Directory.CreateDirectory(releaseRoot);
        foreach (var version in new[] { TargetVersion, BridgeVersion })
        {
            if (!Directory.Exists(fixture.DirectoryFor(version)))
                continue;

            var destination = Path.Combine(releaseRoot, TagPrefix + version);
            Directory.CreateDirectory(destination);
            foreach (var name in new[] { ReleaseProtocol.ManifestName, ReleaseProtocol.SignatureName, ContainerName })
                File.Copy(Path.Combine(fixture.DirectoryFor(version), name), Path.Combine(destination, name));
        }

        var list = workspace.PathFor(ReleasesName);
        await File.WriteAllBytesAsync(list, ReleaseJson.Serialize(releases));
        var driver = workspace.PathFor(DriverName);
        await File.WriteAllTextAsync(driver, Driver);
        var start = new ProcessStartInfo(PowerShell) { WorkingDirectory = workspace.Root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { NoProfileArgument, FileArgument, driver, ScriptArgument, Path.Combine(source, SequenceScript), ToolArgument, typeof(BundleRequest).Assembly.Location, ListArgument, list, RootArgument, releaseRoot })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The sequence allocator could not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("The sequence allocator did not finish within 60 seconds.");
        }

        return (process.ExitCode, await output, await error);
    }

    /// <summary>
    /// Creates GitHub-style independent digests for the fixture's actual signed manifest and metadata container.
    /// </summary>
    /// <param name="fixture">The producer fixture owning the captured bytes.</param>
    /// <param name="version">The advertised release version.</param>
    /// <param name="draft">Whether the release is temporarily unpublished.</param>
    /// <param name="invalidDigest">Whether to corrupt independent asset evidence.</param>
    /// <returns>A minimal release API entry consumed by the production allocator.</returns>
    private static object Release(ReleaseBundleFixture fixture, string version, bool draft = false, bool invalidDigest = false)
    {
        var directory = fixture.DirectoryFor(version);
        object[] assets = [.. new[] { ReleaseProtocol.ManifestName, ReleaseProtocol.SignatureName, ContainerName }.Select(name => new
        {
            name,
            size = new FileInfo(Path.Combine(directory, name)).Length,
            digest = invalidDigest ? InvalidDigest : DigestPrefix + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, name)))).ToLowerInvariant()
        })];
        return new
        {
            tag_name = TagPrefix + version,
            prerelease = true,
            immutable = false,
            draft,
            assets
        };
    }

    /// <summary>
    /// Locates this checkout without depending on the test runner's current directory.
    /// </summary>
    /// <returns>The nearest ancestor containing the canonical production allocator.</returns>
    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SequenceScript)))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("The production sequence allocator was not found above the test output.");
    }
}
