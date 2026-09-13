using Ironmon.Updater.Core;
using System.Text.RegularExpressions;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Defines the single reviewed upstream and branch accepted by a provider.
/// </summary>
public sealed partial class RepositoryPolicy
{

    internal const string Origin = "origin";
    internal const string HeadsPrefix = "refs/heads/";
    internal const string RemoteRef = "refs/remotes/origin/";
    private const string BranchPattern = "\\A[A-Za-z0-9][A-Za-z0-9._/-]*\\z";
    private const string DoubleDot = "..";
    private const string LockSuffix = ".lock";
    private const int MaximumConfigBytes = 65536;
    private const string CoreSection = "[core]";
    private const string OriginSection = "[remote \"origin\"]";
    private const string BranchSectionPrefix = "[branch \"";
    private const string SectionEnd = "\"]";
    private const string CorePrefix = "core.";
    private const string OriginPrefix = "remote.origin.";
    private const string BranchPrefix = "branch.";
    private const string FormatKey = "core.repositoryformatversion";
    private const string BareKey = "core.bare";
    private const string FileModeKey = "core.filemode";
    private const string IgnoreCaseKey = "core.ignorecase";
    private const string SymlinksKey = "core.symlinks";
    private const string LogKey = "core.logallrefupdates";
    private const string AutocrlfKey = "core.autocrlf";
    private const string LongPathsKey = "core.longpaths";
    private const string UrlKey = "remote.origin.url";
    private const string FetchKey = "remote.origin.fetch";
    private const string BranchRemoteKey = "branch.remote";
    private const string MergeKey = "branch.merge";
    private const string RequiredKeys = "core.repositoryformatversion|core.bare|remote.origin.url|remote.origin.fetch|branch.remote|branch.merge";
    private const string FetchSpec = "+refs/heads/*:refs/remotes/origin/*";
    private const string Zero = "0";
    private const string True = "true";
    private const string False = "false";
    private const string Input = "input";

    /// <summary>
    /// Gets the expected remote address: public HTTPS in production or a local path in internal fixtures.
    /// </summary>
    public string Remote { get; }

    /// <summary>
    /// Gets the expected local and upstream branch.
    /// </summary>
    public string Branch { get; }

    /// <summary>
    /// Gets whether this policy belongs to an internal local-only fixture.
    /// </summary>
    internal bool LocalFixture { get; }

    /// <summary>
    /// Initializes a policy from trusted release configuration, not an installed repository.
    /// </summary>
    /// <param name="remote">The approved HTTPS repository address without credentials, query, fragment or a custom port.</param>
    /// <param name="branch">The approved local and upstream branch name.</param>
    public RepositoryPolicy(Uri remote, string branch) : this(remote.AbsoluteUri, branch, false)
    {
        if (remote.Scheme != Uri.UriSchemeHttps || remote.UserInfo.Length != 0 || remote.Query.Length != 0 || remote.Fragment.Length != 0 || !remote.IsDefaultPort)
            throw new ArgumentException(UpdaterText.RepositoryPolicyTheApprovedUpstreamMustBeAPublicHTTPSRepository, nameof(remote));
    }

    /// <summary>
    /// Initializes the shared branch policy with an optional local fixture transport.
    /// </summary>
    /// <param name="remote">The approved remote address; the public constructor validates HTTPS addresses.</param>
    /// <param name="branch">The approved branch name to validate.</param>
    /// <param name="localFixture">Whether local file transport is allowed for a trusted, disposable test upstream.</param>
    internal RepositoryPolicy(string remote, string branch, bool localFixture)
    {
        if (!BranchRegex().IsMatch(branch) || branch.Contains(DoubleDot, StringComparison.Ordinal) || branch.Split('/').Any(segment => segment.Length == 0 || segment.StartsWith('.') || segment.EndsWith('.') || segment.EndsWith(LockSuffix, StringComparison.Ordinal)))
            throw new ArgumentException(UpdaterText.RepositoryPolicyTheApprovedBranchNameIsNotSupported, nameof(branch));

        Remote = remote;
        Branch = branch;
        LocalFixture = localFixture;
    }

    /// <summary>
    /// Checks supported local configuration without letting Git load includes or command settings.
    /// </summary>
    /// <param name="file">The fully qualified local Git configuration file to parse.</param>
    internal void ValidateConfiguration(string file)
    {
        if (new FileInfo(file).Length > MaximumConfigBytes)
            throw new InvalidDataException(UpdaterText.RepositoryPolicyTheRepositoryConfigurationIsTooLarge);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = string.Empty;
        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (line.StartsWith('['))
            {
                if (line.Equals(CoreSection, StringComparison.OrdinalIgnoreCase))
                {
                    section = CorePrefix;
                }
                else if (line.Equals(OriginSection, StringComparison.OrdinalIgnoreCase))
                {
                    section = OriginPrefix;
                }
                else if (line.Equals(BranchSectionPrefix + Branch + SectionEnd, StringComparison.Ordinal))
                {
                    section = BranchPrefix;
                }
                else
                {
                    throw new InvalidDataException(UpdaterText.RepositoryPolicyTheRepositoryHasUnsupportedConfigurationSections);
                }

                continue;
            }

            var pair = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (section.Length == 0 || pair.Length != 2 || pair[1].Contains('\\') || pair[1].Contains('"') || !values.TryAdd(section + pair[0], pair[1]))
                throw new InvalidDataException(UpdaterText.RepositoryPolicyTheRepositoryConfigurationIsAmbiguousOrUnsupported);
        }

        var allowed = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [FormatKey] = [Zero],
            [BareKey] = [False],
            [FileModeKey] = [True, False],
            [IgnoreCaseKey] = [True, False],
            [SymlinksKey] = [True, False],
            [LogKey] = [True],
            [AutocrlfKey] = [True, False, Input],
            [LongPathsKey] = [True, False],
            [UrlKey] = [Remote],
            [FetchKey] = [FetchSpec],
            [BranchRemoteKey] = [Origin],
            [MergeKey] = [HeadsPrefix + Branch]
        };

        foreach (var pair in values)
        {
            if (!allowed.TryGetValue(pair.Key, out var accepted) || !accepted.Contains(pair.Value, StringComparer.Ordinal))
                throw new InvalidDataException(UpdaterText.RepositoryPolicyTheRepositoryContainsUnapprovedGitConfiguration);
        }

        foreach (var key in RequiredKeys.Split('|'))
        {
            if (!values.ContainsKey(key))
                throw new InvalidDataException(UpdaterText.RepositoryPolicyTheRepositoryIsMissingExpectedRemoteOrBranchConfiguration);
        }
    }

    /// <summary>
    /// Gets the generated expression for the supported branch-name characters.
    /// </summary>
    /// <returns>The culture-invariant branch-name expression.</returns>
    [GeneratedRegex(BranchPattern, RegexOptions.CultureInvariant)]
    private static partial Regex BranchRegex();
}
