namespace Ironmon.Updater.Core;

/// <summary>
/// Provides localized prerequisite guidance and installation size explanations.
/// </summary>
public static partial class UpdaterText
{
    /// <summary>
    /// Formats the missing .NET Runtime explanation and retry instructions.
    /// </summary>
    /// <param name="version">The current runtime family.</param>
    /// <param name="previousVersion">The previous runtime family.</param>
    /// <param name="downloadPage">The Microsoft download page for the current family.</param>
    /// <param name="previousDownloadPage">The Microsoft download page for the previous family.</param>
    /// <returns>The localized prerequisite message.</returns>
    public static string SetupTrackerRuntimeUnavailable(object? version, object? previousVersion, object? downloadPage, object? previousDownloadPage)
        => string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(nameof(SetupTrackerRuntimeUnavailable)), version, previousVersion, downloadPage, previousDownloadPage);

    /// <summary>
    /// Gets the accessible runtime help action name and dialog title.
    /// </summary>
    public static string SetupTrackerRuntimeHelp => Get(nameof(SetupTrackerRuntimeHelp));

    /// <summary>
    /// Gets the explanation of everything already included in the Ironmon download size.
    /// </summary>
    public static string SetupReviewIronmonSizeHint => Get(nameof(SetupReviewIronmonSizeHint));

    /// <summary>
    /// Gets the distinction between future run-history storage and the installation download.
    /// </summary>
    public static string SetupReviewRunHistorySpace => Get(nameof(SetupReviewRunHistorySpace));
}
