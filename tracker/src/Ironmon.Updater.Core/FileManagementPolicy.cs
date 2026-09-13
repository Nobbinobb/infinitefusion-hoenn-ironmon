using System.Text.RegularExpressions;

namespace Ironmon.Updater.Core;

/// <summary>
/// Bounds package ownership and protects user data independently of release-provided flags.
/// </summary>
public sealed partial class FileManagementPolicy
{
    /// <summary>
    /// Names the sole Ironmon-owned script outside its normal script directory.
    /// </summary>
    public const string BootstrapPath = "Data/Scripts/000_Ironmon_Guard.rb";
    private const string TrackerRoot = "Ironmon Tracker";
    private const string ScriptRoot = "Data/Scripts/997_Ironmon";
    private const string DataRoot = "Data/Ironmon";
    private const string ProfileRoot = "Data/Ironmon/generation_profiles";
    private const string ProtectedRoots = ".git|.ironmon-update|Data/Ironmon/unavailable_sprite_sheets.json|Data/Ironmon/custom_sprite_sheet_sync.json|Graphics/CustomBattlers/spritesheets|Graphics/CustomBattlers/local_sprites/IronmonTracker";
    private const string ProtectedSegments = "saves|save|runs|settings|diagnostics|.git|.ironmon-update";
    private const string LegacyDocuments = "README.md|LICENSE|INSTALLATION.md|RELEASE_NOTES.md|THIRD_PARTY_NOTICES.md|OPEN-SANS-LICENSE.txt";
    private const string SavePattern = "\\A(?:(?:Game|File [^/]+|Save[^/]*|IronmonCheckpoint_[^/]+|Autosave[^/]*)\\.rxdata(?:\\..*)?|.+\\.(?:sav|rvdata|rvdata2)(?:\\..*)?)\\z";
    private static readonly string[] _ironmonRoots = [TrackerRoot, ScriptRoot, DataRoot];
    private static readonly HashSet<string> _protectedSegments = new(ProtectedSegments.Split('|'), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _legacyDocuments = new(LegacyDocuments.Split('|'), StringComparer.OrdinalIgnoreCase);
    private readonly string[] _protectedPaths;

    /// <summary>
    /// Copies the protections so a persisted plan cannot silently lose installation-specific exclusions.
    /// </summary>
    /// <returns>A detached list including mandatory protected roots.</returns>
    internal string[] ExportProtectedPaths()
        => [.. _protectedPaths];

    /// <summary>
    /// Initializes mandatory protections and any additional runtime-discovered user-data destinations.
    /// </summary>
    /// <param name="additionalProtectedPaths">Trusted installation-relative protected files or directories; release metadata cannot weaken these protections.</param>
    public FileManagementPolicy(IEnumerable<string>? additionalProtectedPaths = null)
    {
        _protectedPaths = [.. ProtectedRoots.Split('|').Concat(additionalProtectedPaths ?? []).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)];
        foreach (var path in _protectedPaths)
            UpdateFilePath.Validate(path);
    }

    /// <summary>
    /// Rejects ownership claims outside authorized paths or over protected state.
    /// </summary>
    /// <param name="file">The authenticated inventory entry.</param>
    /// <param name="target">Whether this is a new release claim rather than a legacy baseline.</param>
    internal void Validate(ManagedFile file, bool target)
    {
        UpdateFilePath.Validate(file.Path);
        UpdateFilePath.ValidateContent(file.Content);
        if (file.AlternateContent is not null)
            UpdateFilePath.ValidateContent(file.AlternateContent);

        if (!Enum.IsDefined(file.Owner) || !Enum.IsDefined(file.Policy))
            throw new InvalidDataException(UpdaterText.FileManagementPolicyTheInventoryContainsAnUnsupportedOwnershipPolicy);

        if (file.Policy == ManagedFilePolicy.Retain && file.AlternateContent is not null)
            throw new InvalidDataException(UpdaterText.FileManagementPolicyImmutableFilesMustSpecifyOneExactByteRepresentation);

        if (IsProtected(file.Path) || _protectedPaths.Any(path => UpdateFilePath.Within(path, file.Path)))
            throw new InvalidDataException(UpdaterText.FileManagementPolicyTheReleaseInventoryOverlapsProtectedUserOrUpdaterData(file.Path));

        if (UpdateFilePath.Within(ProfileRoot, file.Path) || (UpdateFilePath.Within(file.Path, ProfileRoot) && (file.Policy != ManagedFilePolicy.Retain || file.Owner != ManagedFileOwner.Ironmon)))
            throw new InvalidDataException(UpdaterText.FileManagementPolicyHistoricalGenerationProfilesRequireImmutableFileOwnership);

        if (file.Owner == ManagedFileOwner.Ironmon)
        {
            if (file.Path != BootstrapPath && !_ironmonRoots.Any(root => UpdateFilePath.Within(file.Path, root) && !file.Path.Equals(root, StringComparison.OrdinalIgnoreCase)) && (target || !IsLegacyDocument(file)))
                throw new InvalidDataException(UpdaterText.FileManagementPolicyTheIronmonInventoryClaimsAFileOutsideItsManaged);
        }
        else if (UpdateFilePath.Within(BootstrapPath, file.Path) || UpdateFilePath.Within(file.Path, BootstrapPath) || _ironmonRoots.Any(root => UpdateFilePath.Within(file.Path, root) || UpdateFilePath.Within(root, file.Path)))
        {
            throw new InvalidDataException(UpdaterText.FileManagementPolicyAGameFileOverlapsAnIronmonOwnedDestination);
        }
    }

    /// <summary>
    /// Recognizes runtime data that must survive even beside managed program files.
    /// </summary>
    /// <param name="path">The validated relative path.</param>
    /// <returns>Whether the path belongs to protected state.</returns>
    internal bool IsProtected(string path)
        => _protectedPaths.Any(root => UpdateFilePath.Within(path, root)) || path.Split('/').Any(segment => _protectedSegments.Contains(segment) || SaveRegex().IsMatch(segment));

    /// <summary>
    /// Identifies legacy root documents whose old package entries cannot authorize deletion.
    /// </summary>
    /// <param name="file">The prior package entry.</param>
    /// <returns>Whether the entry must be preserved under the legacy-document rule.</returns>
    internal static bool IsLegacyDocument(ManagedFile file)
        => file.Owner == ManagedFileOwner.Ironmon && _legacyDocuments.Contains(file.Path);

    /// <summary>
    /// Gets the generated expression for known save slots, checkpoints and save backups.
    /// </summary>
    /// <returns>The case-insensitive save filename expression.</returns>
    [GeneratedRegex(SavePattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SaveRegex();
}
