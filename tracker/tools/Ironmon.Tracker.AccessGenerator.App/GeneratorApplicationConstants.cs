namespace Ironmon.Tracker.AccessGenerator.App;

/// <summary>
/// Defines stable native application and token-form configuration values.
/// </summary>
internal static class GeneratorApplicationConstants
{
    /// <summary>
    /// Gets the initial window width.
    /// </summary>
    internal const double DefaultWindowWidth = 980;

    /// <summary>
    /// Gets the initial window height.
    /// </summary>
    internal const double DefaultWindowHeight = 900;

    /// <summary>
    /// Gets the minimum supported window width.
    /// </summary>
    internal const double MinimumWindowWidth = 760;

    /// <summary>
    /// Gets the minimum supported window height.
    /// </summary>
    internal const double MinimumWindowHeight = 620;

    /// <summary>
    /// Gets the bundled primary font filename.
    /// </summary>
    internal const string FontFile = "OpenSans-Regular.ttf";

    /// <summary>
    /// Gets the primary font alias.
    /// </summary>
    internal const string FontAlias = "GeneratorSans";

    /// <summary>
    /// Gets the private-key file extension accepted by the generator.
    /// </summary>
    internal const string PrivateKeyFileExtension = ".pem";

    /// <summary>
    /// Gets the first-run temporary-access duration in days.
    /// </summary>
    internal const int DefaultExpirationDays = 7;

    /// <summary>
    /// Gets the invariant HTML local-date-time input format.
    /// </summary>
    internal const string LocalDateTimeInputFormat = "yyyy-MM-ddTHH:mm:ss";

    /// <summary>
    /// Gets the normalized browser date format when seconds are omitted.
    /// </summary>
    internal const string LocalDateTimeMinuteInputFormat = "yyyy-MM-ddTHH:mm";

    /// <summary>
    /// Gets the invariant UTC expiration display format.
    /// </summary>
    internal const string UtcExpirationDisplayFormat = "yyyy-MM-dd HH:mm:ss 'UTC'";

    /// <summary>
    /// Gets the compact GUID format used for JWT IDs.
    /// </summary>
    internal const string TokenIdFormat = "N";

    /// <summary>
    /// Gets the filename prefix used for saved access tokens.
    /// </summary>
    internal const string TokenFileNamePrefix = "ironmon-access-";
}
