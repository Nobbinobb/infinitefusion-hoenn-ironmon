namespace Ironmon.Tracker.Connection.Access;

/// <summary>
/// Validates, persists, expires, replaces, and removes the tracker's single diagnostic-access token.
/// </summary>
public sealed class DiagnosticAccessService : IDisposable
{
    private const string _persistedTokenReadError = "The persisted diagnostic access token could not be read.";
    private const string _persistedTokenWriteError = "The diagnostic access token could not be saved.";
    private const string _tokenExpiredMessage = "Diagnostic access token has expired.";
    private readonly bool _developerOverride;
    private readonly string _path;
    private readonly Lock _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly DiagnosticAccessTokenValidator _validator;
    private ITimer? _expirationTimer;
    private DiagnosticAccessSnapshot _snapshot;

    /// <summary>
    /// Initializes and loads tracker-owned diagnostic access.
    /// </summary>
    /// <param name="options">The tracker-owned persistence location.</param>
    /// <param name="validator">The signed-token validator.</param>
    /// <param name="developerOverride">Whether this local build grants every supported capability without a token.</param>
    /// <param name="timeProvider">The UTC time source, or the system source when omitted.</param>
    public DiagnosticAccessService(TrackerKnowledgeOptions options, DiagnosticAccessTokenValidator validator, bool developerOverride = false, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validator);
        _validator = validator;
        _developerOverride = developerOverride;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _path = Path.Combine(options.RootDirectory, TrackerStorageNames.SettingsDirectory, TrackerStorageNames.DiagnosticAccessTokenFile);
        _snapshot = developerOverride ? DiagnosticAccessSnapshot.DeveloperOverride() : Load();
        ScheduleExpiration(_snapshot);
    }

    /// <summary>
    /// Occurs after effective diagnostic access changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the current immutable diagnostic-access state.
    /// </summary>
    public DiagnosticAccessSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return _snapshot;
        }
    }

    /// <summary>
    /// Validates and atomically activates one token without replacing valid access on failure.
    /// </summary>
    /// <param name="token">The pasted or imported compact token.</param>
    /// <returns>The validation result.</returns>
    /// <exception cref="IOException">Thrown when a valid token cannot be persisted.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when tracker-owned persistence is unavailable.</exception>
    public async Task<DiagnosticAccessValidationResult> ActivateAsync(string? token)
    {
        string normalized = token?.Trim() ?? string.Empty;
        DiagnosticAccessValidationResult result = await _validator.ValidateAsync(normalized);
        if (!result.IsValid || result.Grant is null)
            return result;

        if (_developerOverride)
            return result;

        await PersistAsync(normalized);
        lock (_sync)
        {
            _snapshot = DiagnosticAccessSnapshot.Active(result.Grant, result.UnknownCapabilities);
            ScheduleExpiration(_snapshot);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <summary>
    /// Removes persisted access and immediately clears every token capability.
    /// </summary>
    /// <returns>Whether persisted or effective state changed.</returns>
    /// <exception cref="IOException">Thrown when the persisted token cannot be removed.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when tracker-owned persistence is unavailable.</exception>
    public bool Remove()
    {
        if (_developerOverride)
            return false;

        bool existed = File.Exists(_path);
        if (existed)
            File.Delete(_path);

        bool changed;
        lock (_sync)
        {
            changed = _snapshot.State != DiagnosticAccessState.None;
            _expirationTimer?.Dispose();
            _expirationTimer = null;
            _snapshot = DiagnosticAccessSnapshot.None();
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);

        return existed || changed;
    }

    /// <summary>
    /// Re-evaluates expiration against the configured UTC clock.
    /// </summary>
    /// <returns>Whether active access expired.</returns>
    public bool RefreshExpiration()
    {
        DiagnosticAccessSnapshot? expired = null;
        lock (_sync)
        {
            if (_snapshot.State == DiagnosticAccessState.Active && _snapshot.Grant?.ExpiresAt is DateTimeOffset expiresAt && _timeProvider.GetUtcNow() >= expiresAt)
            {
                expired = DiagnosticAccessSnapshot.Expired(_snapshot.Grant, _snapshot.UnknownCapabilities, _tokenExpiredMessage);
                _expirationTimer?.Dispose();
                _expirationTimer = null;
                _snapshot = expired;
            }
        }

        if (expired is not null)
            Changed?.Invoke(this, EventArgs.Empty);

        return expired is not null;
    }

    /// <summary>
    /// Loads and validates the persisted token without exposing it through tracker state.
    /// </summary>
    /// <returns>The restored access state.</returns>
    private DiagnosticAccessSnapshot Load()
    {
        if (!File.Exists(_path))
            return DiagnosticAccessSnapshot.None();

        try
        {
            string token = File.ReadAllText(_path).Trim();
            DiagnosticAccessValidationResult result = _validator.ValidateAsync(token).GetAwaiter().GetResult();
            if (result.IsValid && result.Grant is not null)
                return DiagnosticAccessSnapshot.Active(result.Grant, result.UnknownCapabilities);

            return result.Status == DiagnosticAccessValidationStatus.Expired
                ? DiagnosticAccessSnapshot.Expired(null, result.UnknownCapabilities, result.Message)
                : DiagnosticAccessSnapshot.Invalid(result.Status, result.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DiagnosticAccessSnapshot.Invalid(null, _persistedTokenReadError);
        }
    }

    /// <summary>
    /// Atomically writes one validated token beneath tracker-owned local application data.
    /// </summary>
    /// <param name="token">The normalized compact token.</param>
    /// <returns>A task representing durable persistence.</returns>
    private async Task PersistAsync(string token)
    {
        string directory = Path.GetDirectoryName(_path) ?? throw new IOException(_persistedTokenWriteError);
        Directory.CreateDirectory(directory);
        string temporaryPath = $"{_path}{TrackerStorageNames.TemporaryExtension}";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, token);
            File.Move(temporaryPath, _path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
            }

            throw new IOException(_persistedTokenWriteError, exception);
        }
    }

    /// <summary>
    /// Schedules immediate capability removal at the active token's expiration instant.
    /// </summary>
    /// <param name="snapshot">The state whose expiration should be scheduled.</param>
    private void ScheduleExpiration(DiagnosticAccessSnapshot snapshot)
    {
        _expirationTimer?.Dispose();
        _expirationTimer = null;
        if (snapshot.State != DiagnosticAccessState.Active || snapshot.Grant?.ExpiresAt is not DateTimeOffset expiresAt)
            return;

        TimeSpan dueTime = expiresAt - _timeProvider.GetUtcNow();
        if (dueTime < TimeSpan.Zero)
            dueTime = TimeSpan.Zero;

        _expirationTimer = _timeProvider.CreateTimer(static state => ((DiagnosticAccessService)state!).RefreshExpiration(), this, dueTime, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Releases the active expiration timer.
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            _expirationTimer?.Dispose();
            _expirationTimer = null;
        }

        GC.SuppressFinalize(this);
    }
}
