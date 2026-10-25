namespace Ironmon.Updater.Core;

/// <summary>
/// Provides localized prerequisite guidance and installation size explanations.
/// </summary>
public static partial class UpdaterText
{
    /// <summary>
    /// Gets the missing .NET Desktop Runtime explanation and restart instructions.
    /// </summary>
    public static string SetupTrackerRuntimeUnavailable => Get(nameof(SetupTrackerRuntimeUnavailable));

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
