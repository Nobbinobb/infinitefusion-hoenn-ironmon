using System.Globalization;

namespace Ironmon.Updater.Core;

/// <summary>
/// Provides localized messages for explicit historical installer targets and their availability checks.
/// </summary>
public static partial class UpdaterText
{
    /// <summary>
    /// Gets the availability-check progress message.
    /// </summary>
    public static string SetupVersionsLoading => Get(nameof(SetupVersionsLoading));

    /// <summary>
    /// Gets the retryable availability failure explanation.
    /// </summary>
    public static string SetupVersionsUnavailable => Get(nameof(SetupVersionsUnavailable));

    /// <summary>
    /// Gets the explanation for releases without supported authenticated installation metadata.
    /// </summary>
    public static string SetupVersionsMissingMetadata => Get(nameof(SetupVersionsMissingMetadata));

    /// <summary>
    /// Gets the explanation for a release requiring a newer installer engine.
    /// </summary>
    public static string SetupVersionsNewerSetupRequired => Get(nameof(SetupVersionsNewerSetupRequired));

    /// <summary>
    /// Gets the explanation for an unavailable or unverifiable pinned game revision.
    /// </summary>
    public static string SetupVersionsGameUnavailable => Get(nameof(SetupVersionsGameUnavailable));

    /// <summary>
    /// Gets the explanation for unavailable installation payloads.
    /// </summary>
    public static string SetupVersionsDownloadsUnavailable => Get(nameof(SetupVersionsDownloadsUnavailable));

    /// <summary>
    /// Gets the instruction to repeat review when a selected release changes.
    /// </summary>
    public static string SetupVersionsReleaseChanged => Get(nameof(SetupVersionsReleaseChanged));

    /// <summary>
    /// Gets the advanced version-selection heading.
    /// </summary>
    public static string SetupVersionsAdvanced => Get(nameof(SetupVersionsAdvanced));

    /// <summary>
    /// Gets the automatic latest-release choice.
    /// </summary>
    public static string SetupVersionsLatest => Get(nameof(SetupVersionsLatest));

    /// <summary>
    /// Gets the current-availability refresh action.
    /// </summary>
    public static string SetupVersionsRefresh => Get(nameof(SetupVersionsRefresh));

    /// <summary>
    /// Gets the historical installation and availability explanation.
    /// </summary>
    public static string SetupVersionsHint => Get(nameof(SetupVersionsHint));

    /// <summary>
    /// Gets the instruction to select an available installer target.
    /// </summary>
    public static string SetupVersionsSelectAvailable => Get(nameof(SetupVersionsSelectAvailable));

    /// <summary>
    /// Gets the completed catalog-selection status.
    /// </summary>
    public static string SetupVersionsChooseVersion => Get(nameof(SetupVersionsChooseVersion));

    /// <summary>
    /// Formats a release with its independently authenticated game display version.
    /// </summary>
    /// <param name="version">The Ironmon release version.</param>
    /// <param name="game">The signed Infinite Fusion display version.</param>
    /// <returns>The release selection label.</returns>
    public static string SetupVersionsChoice(string version, string game)
        => string.Format(CultureInfo.CurrentCulture, Get(nameof(SetupVersionsChoice)), version, game);

    /// <summary>
    /// Labels an explicitly selected prerelease whose files may change during testing.
    /// </summary>
    /// <param name="label">The verified availability label.</param>
    /// <returns>The label marked as a test release.</returns>
    public static string SetupVersionsTestRelease(string label)
        => string.Format(CultureInfo.CurrentCulture, Get(nameof(SetupVersionsTestRelease)), label);

    /// <summary>
    /// Formats an unavailable version without claiming a verified game mapping.
    /// </summary>
    /// <param name="version">The historical Ironmon version.</param>
    /// <returns>The disabled selection label.</returns>
    public static string SetupVersionsUnavailableChoice(string version)
        => string.Format(CultureInfo.CurrentCulture, Get(nameof(SetupVersionsUnavailableChoice)), version);
}
