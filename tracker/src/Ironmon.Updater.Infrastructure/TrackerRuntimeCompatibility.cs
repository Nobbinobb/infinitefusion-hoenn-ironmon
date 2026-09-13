using Ironmon.Updater.Core;
using System.Text.Json;
using Microsoft.Win32;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Checks that a verified tracker package can run before replacing the current installation.
/// </summary>
public interface ITrackerRuntimeCompatibility
{
    /// <summary>
    /// Requires the selected package flavor and its signed runtime configuration to agree with available frameworks.
    /// </summary>
    /// <param name="packageRoot">The verified package or completed installation root.</param>
    /// <param name="flavor">The authenticated deployment flavor.</param>
    /// <param name="cancellationToken">The verification token.</param>
    /// <returns>A task that fails before commit if the tracker cannot use its required runtime.</returns>
    Task EnsureAsync(string packageRoot, string flavor, CancellationToken cancellationToken);
}

/// <summary>
/// Checks signed runtime configuration against installed Windows x64 shared frameworks without invoking system PATH tools.
/// </summary>
public sealed class TrackerRuntimeCompatibility : ITrackerRuntimeCompatibility
{
    internal const string ConfigurationPath = "Ironmon Tracker/Ironmon Tracker.runtimeconfig.json";
    private const string CoreRuntimePath = "Ironmon Tracker/coreclr.dll";
    private const string RuntimeOptions = "runtimeOptions";
    private const string Framework = "framework";
    private const string Frameworks = "frameworks";
    private const string IncludedFrameworks = "includedFrameworks";
    private const string Name = "name";
    private const string VersionProperty = "version";
    private const string RollForward = "rollForward";
    private const string Disable = "Disable";
    private const string LatestPatch = "LatestPatch";
    private const string Minor = "Minor";
    private const string RuntimeRegistry = @"SOFTWARE\dotnet\Setup\InstalledVersions\x64";
    private const string InstallLocation = "InstallLocation";
    private const string SharedDirectory = "shared";
    private const string KnownFrameworks = "Microsoft.NETCore.App|Microsoft.WindowsDesktop.App|Microsoft.AspNetCore.App";
    private static readonly HashSet<string> _frameworkNames = new(KnownFrameworks.Split('|'), StringComparer.Ordinal);
    private readonly Func<string, IReadOnlyList<Version>> _installed;

    /// <summary>
    /// Creates a runtime prerequisite checker using trusted Windows x64 installation registration.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public TrackerRuntimeCompatibility() : this(InstalledVersions)
    {
    }

    /// <summary>
    /// Creates an isolated runtime availability fixture without executing downloaded programs.
    /// </summary>
    /// <param name="installed">The framework version lookup.</param>
    internal TrackerRuntimeCompatibility(Func<string, IReadOnlyList<Version>> installed)
    {
        _installed = installed;
    }

    /// <summary>
    /// Checks flavor consistency and conservatively requires the declared framework major/minor with a compatible patch.
    /// </summary>
    /// <param name="packageRoot">The verified extraction or installation directory.</param>
    /// <param name="flavor">The signed flavor.</param>
    /// <param name="cancellationToken">The verification token.</param>
    /// <returns>A completed check or an actionable missing-runtime error.</returns>
    public Task EnsureAsync(string packageRoot, string flavor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = TransactionStorage.ReadBytes(PlainPaths.Child(packageRoot, ConfigurationPath));
        ReleaseJson.Validate(bytes, 256 * 1024);
        using var document = JsonDocument.Parse(bytes);
        var options = document.RootElement.GetProperty(RuntimeOptions);
        var hasFramework = options.TryGetProperty(Framework, out var framework);
        var hasFrameworks = options.TryGetProperty(Frameworks, out var frameworks);
        if (flavor == ReleaseProtocol.SelfContained)
        {
            if (hasFramework || hasFrameworks || !options.TryGetProperty(IncludedFrameworks, out _) || !File.Exists(PlainPaths.Child(packageRoot, CoreRuntimePath)))
                throw new InvalidDataException(UpdaterText.TrackerRuntimeCompatibilityTheSelfContainedTrackerPackageDoesNotContainIts);

            return Task.CompletedTask;
        }

        if (flavor != ReleaseProtocol.RuntimeRequired || hasFramework == hasFrameworks)
            throw new InvalidDataException(UpdaterText.TrackerRuntimeCompatibilityTheRuntimeRequiredPackageHasNoUnambiguousFrameworkConfiguration);

        var requirements = hasFramework ? [framework] : frameworks.EnumerateArray().ToArray();
        if (requirements.Length is < 1 or > 3)
            throw new InvalidDataException(UpdaterText.TrackerRuntimeCompatibilityTheTrackerDeclaresAnUnsupportedFrameworkSet);

        var rollForward = options.TryGetProperty(RollForward, out var policy) ? policy.GetString() : Minor;
        if (rollForward is not (Disable or LatestPatch or Minor))
            throw new InvalidDataException(UpdaterText.TrackerRuntimeCompatibilityThisTrackerRuntimePolicyRequiresANewerVerifiedInstaller);

        foreach (var requirement in requirements)
        {
            var name = requirement.GetProperty(Name).GetString() ?? throw new InvalidDataException(UpdaterText.TrackerRuntimeCompatibilityATrackerFrameworkNameIsMissing);
            var required = ReleaseProtocol.ParseVersion(requirement.GetProperty(VersionProperty).GetString()!);
            if (!_frameworkNames.Contains(name) || !_installed(name).Any(version => version.Major == required.Major && version.Minor == required.Minor && (rollForward == Disable ? version == required : version >= required)))
                throw new InvalidOperationException(UpdaterText.TrackerRuntimeCompatibilityThisTrackerRequiresForWindowsX64InstallThatRuntime(name, required));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads stable framework directories beneath the registered x64 .NET installation.
    /// </summary>
    /// <remarks>
    /// Windows registers all .NET architectures in the 32-bit registry view; the x64 subkey selects the architecture.
    /// </remarks>
    /// <param name="framework">The fixed known shared framework name.</param>
    /// <returns>The available stable framework versions.</returns>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IReadOnlyList<Version> InstalledVersions(string framework)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var key = machine.OpenSubKey(RuntimeRegistry);
        if (key?.GetValue(InstallLocation) is not string root)
            return [];

        var shared = PlainPaths.Child(PlainPaths.Full(root), SharedDirectory + '/' + framework);
        if (!Directory.Exists(shared))
            return [];

        var versions = new List<Version>();
        foreach (var path in Directory.EnumerateDirectories(shared))
        {
            PlainPaths.Full(path);
            if (Version.TryParse(Path.GetFileName(path), out var version) && version.Build >= 0 && version.Revision < 0)
                versions.Add(version);
        }

        return versions;
    }
}
