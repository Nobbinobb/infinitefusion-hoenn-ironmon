using Ironmon.Updater.Core;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Authenticates and reconstructs a private MinGit runtime without system installation.
/// </summary>
public sealed class MinGitCache
{
    private readonly string _root;
    private readonly IArtifactSource _source;
    private readonly MinGitPackage _package;
    private readonly TimeProvider _clock;
    private const string ArchiveName = "package.zip";
    private const string ToolName = "runtime";
    private const string LockName = "acquire.lock";
    private const string StagePrefix = "stage-";
    private const string TemporaryArchive = "download.zip";
    private const string Wildcard = "*";
    private const long MaximumExpandedBytes = 512L * 1024 * 1024;
    private const int MaximumEntries = 10000;

    /// <summary>
    /// Gets the dedicated cache root for installation-boundary checks.
    /// </summary>
    internal string Root => _root;

    /// <summary>
    /// Initializes a cache scoped to a dedicated updater-owned directory.
    /// </summary>
    /// <param name="cacheRoot">The fully qualified directory reserved for the private Git cache.</param>
    /// <param name="source">The artifact source; the caller retains ownership.</param>
    /// <param name="clock">The deadline and retry clock, or <see langword="null" /> to use the system clock.</param>
    public MinGitCache(string cacheRoot, IArtifactSource source, TimeProvider? clock = null) : this(cacheRoot, source, MinGitPackage.Pinned, clock ?? TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a cache with an explicit package pin and clock, allowing synthetic archives in tests.
    /// </summary>
    /// <param name="cacheRoot">The fully qualified directory reserved for the private Git cache.</param>
    /// <param name="source">The artifact source; the caller retains ownership.</param>
    /// <param name="package">The trusted archive address, size and digest to enforce.</param>
    /// <param name="clock">The clock used for acquisition deadlines and lock retries.</param>
    internal MinGitCache(string cacheRoot, IArtifactSource source, MinGitPackage package, TimeProvider clock)
    {
        _root = PlainPaths.Full(cacheRoot);
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _package = package;
        _clock = clock;
    }

    /// <summary>
    /// Returns a verified executable path, repairing only this private cache if necessary.
    /// </summary>
    /// <remarks>
    /// Checks the archive pin and all runtime files on every acquisition. Cache access is serialized across processes; the overall operation has a five-minute deadline.
    /// </remarks>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task whose result is the fully qualified path to the verified private Git executable.</returns>
    /// <exception cref="OperationCanceledException">The caller cancels or the acquisition deadline expires.</exception>
    /// <exception cref="InvalidDataException">The downloaded archive fails authentication or contains unsupported entries.</exception>
    public async Task<string> AcquireAsync(CancellationToken cancellationToken = default)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5), _clock);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        cancellationToken = operation.Token;
        cancellationToken.ThrowIfCancellationRequested();
        PlainPaths.Full(_root);
        Directory.CreateDirectory(_root);
        await using var cacheLock = await LockAsync(cancellationToken).ConfigureAwait(false);
        var archive = PlainPaths.Child(_root, ArchiveName);
        var runtime = PlainPaths.Child(_root, ToolName);
        var stage = PlainPaths.Child(_root, StagePrefix + Guid.NewGuid().ToString());
        Directory.CreateDirectory(stage);
        try
        {
            if (!await IsAuthenticAsync(archive, cancellationToken).ConfigureAwait(false))
            {
                var partial = PlainPaths.Child(stage, TemporaryArchive);
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await _source.CopyToAsync(_package.Source, output, _package.Size, cancellationToken).ConfigureAwait(false);

                if (!await IsAuthenticAsync(partial, cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException(UpdaterText.MinGitCacheTheMinGitArchiveFailedSizeOrSHA256Verification);

                File.Move(partial, archive, overwrite: true);
            }

            using var zip = ZipFile.OpenRead(archive);
            ValidateEntries(zip, stage);
            if (await MatchesAsync(zip, runtime, cancellationToken).ConfigureAwait(false))
                return PlainPaths.Child(runtime, MinGitPackage.Executable);

            var extracted = PlainPaths.Child(stage, ToolName);
            Directory.CreateDirectory(extracted);
            foreach (var entry in zip.Entries.Where(entry => !entry.FullName.EndsWith('/')))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = PlainPaths.Child(extracted, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = entry.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            if (!await MatchesAsync(zip, extracted, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException(UpdaterText.MinGitCacheTheExtractedGitRuntimeFailedVerification);

            cancellationToken.ThrowIfCancellationRequested();
            PlainPaths.DeleteOwned(_root, runtime);
            Directory.Move(extracted, runtime);
            return PlainPaths.Child(runtime, MinGitPackage.Executable);
        }
        finally
        {
            PlainPaths.DeleteOwned(_root, stage);
        }
    }

    /// <summary>
    /// Serializes acquisition across processes, with a bounded wait.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task whose result is an exclusively opened lock stream; the caller must dispose it to release the lock.</returns>
    /// <exception cref="IOException">The lock file still cannot be opened after thirty seconds of retries.</exception>
    private async Task<FileStream> LockAsync(CancellationToken cancellationToken)
    {
        var started = _clock.GetTimestamp();
        var path = PlainPaths.Child(_root, LockName);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (_clock.GetElapsedTime(started) < TimeSpan.FromSeconds(30))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), _clock, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Checks a compressed artifact against the pinned size and digest before opening it as a ZIP.
    /// </summary>
    /// <param name="path">The fully qualified archive path to inspect.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task whose result is whether the file exists and matches both the pinned size and SHA-256 digest.</returns>
    private async Task<bool> IsAuthenticAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != _package.Size)
            return false;

        PlainPaths.Full(path);
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(_package.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Rejects collisions, redirects and expansion bombs before extraction.
    /// </summary>
    /// <param name="zip">The authenticated archive to inspect.</param>
    /// <param name="root">The fully qualified staging root used to validate entry destinations.</param>
    private static void ValidateEntries(ZipArchive zip, string root)
    {
        if (zip.Entries.Count > MaximumEntries)
            throw new InvalidDataException(UpdaterText.MinGitCacheTheArchiveHasTooManyEntries);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.TrimEnd('/');
            PlainPaths.Child(root, name);
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (!names.Add(name) || unixType is not (0 or 0x8000 or 0x4000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(UpdaterText.MinGitCacheTheArchiveContainsADuplicateOrUnsupportedEntry);

            expanded = checked(expanded + entry.Length);
            if (expanded > MaximumExpandedBytes)
                throw new InvalidDataException(UpdaterText.MinGitCacheTheExpandedGitArchiveIsTooLarge);
        }

        if (!names.Contains(MinGitPackage.Executable) || !names.Contains(MinGitPackage.License))
            throw new InvalidDataException(UpdaterText.MinGitCacheTheGitArchiveIsMissingItsExecutableOrLicense);
    }

    /// <summary>
    /// Checks every cached file against the authenticated ZIP, including extra-file injection.
    /// </summary>
    /// <param name="zip">The authenticated archive containing the expected files.</param>
    /// <param name="runtime">The fully qualified runtime directory to compare.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task whose result is whether every expected file matches and no additional files exist.</returns>
    private static async Task<bool> MatchesAsync(ZipArchive zip, string runtime, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(runtime))
            return false;

        PlainPaths.CheckTree(runtime);
        var entries = zip.Entries.Where(entry => !entry.FullName.EndsWith('/')).ToList();
        if (Directory.EnumerateFiles(runtime, Wildcard, SearchOption.AllDirectories).Count() != entries.Count)
            return false;

        foreach (var entry in entries)
        {
            var path = PlainPaths.Child(runtime, entry.FullName);
            if (!File.Exists(path) || new FileInfo(path).Length != entry.Length)
                return false;

            await using var original = entry.Open();
            await using var cached = File.OpenRead(path);
            var expected = await SHA256.HashDataAsync(original, cancellationToken).ConfigureAwait(false);
            var actual = await SHA256.HashDataAsync(cached, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                return false;
        }

        return true;
    }
}
