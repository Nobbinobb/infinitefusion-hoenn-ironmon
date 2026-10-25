using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Provides bounded serialization, verified copies and flushed same-volume journal replacement.
/// </summary>
internal static class TransactionStorage
{
    internal const string DescriptionFile = "description.json";
    internal const string JournalFile = "journal.json";
    internal const string PreviousSuffix = ".previous";
    internal const string TransactionsDirectory = "transactions";
    internal const string BackupDirectory = "backup";
    internal const string PayloadDirectory = "payload";
    internal const string GitDirectory = ".git";
    internal const string GitBackupDirectory = "git-backup";
    internal const string GitPreparedDirectory = "git-prepared";
    internal const string GitRetiredDirectory = "git-retired";
    internal const string GitRestoreDirectory = "git-restore";
    internal const string InstallationFile = "installation.json";
    internal const string GuidFormat = "D";
    internal const int FileConcurrency = 4;
    private const string TemporarySuffix = ".temporary";
    private const int MaximumDocumentBytes = 64 * 1024 * 1024;
    private const int MaximumTreeEntries = 200000;
    private const int _contentionRetries = 10;
    private const int _contentionRetryMilliseconds = 100;
    private const int _accessDenied = 5;
    private const int _sharingViolation = 32;
    private const int _lockViolation = 33;
    private const int _unableToRemoveReplaced = 1175;
    private const int _win32ErrorMask = 0xffff;
    private static readonly JsonSerializerOptions _jsonOptions = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true, MaxDepth = 32 };

    /// <summary>
    /// Serializes an envelope using one cached strict contract.
    /// </summary>
    /// <typeparam name="T">The envelope type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The exact UTF-8 bytes for authorization or hashing.</returns>
    internal static byte[] Serialize<T>(T value)
        => JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions);

    /// <summary>
    /// Reads bounded JSON and rejects duplicate properties at every nesting level.
    /// </summary>
    /// <typeparam name="T">The expected closed contract.</typeparam>
    /// <param name="bytes">The exact persisted JSON.</param>
    /// <returns>The non-null deserialized envelope.</returns>
    internal static T Deserialize<T>(byte[] bytes)
    {
        if (bytes.Length > MaximumDocumentBytes)
            throw new InvalidDataException(UpdaterText.TransactionStorageTheRecoveryDocumentExceedsItsSizeLimit);

        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        RejectDuplicates(document.RootElement);
        return JsonSerializer.Deserialize<T>(bytes, _jsonOptions) ?? throw new InvalidDataException(UpdaterText.TransactionStorageTheRecoveryDocumentIsEmpty);
    }

    /// <summary>
    /// Rejects ambiguous object keys before deserialization.
    /// </summary>
    /// <param name="element">The current JSON value.</param>
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException(UpdaterText.TransactionStorageDuplicateRecoveryDocumentPropertiesAreNotSupported);

                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                RejectDuplicates(child);
        }
    }

    /// <summary>
    /// Reads a bounded file without allowing a concurrent writer.
    /// </summary>
    /// <param name="path">The checked file.</param>
    /// <returns>The exact bytes.</returns>
    internal static byte[] ReadBytes(string path)
    {
        using var stream = OpenRead(path);
        if (stream.Length > MaximumDocumentBytes)
            throw new InvalidDataException(UpdaterText.TransactionStorageTheRecoveryDocumentExceedsItsSizeLimit);

        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>
    /// Gets a SHA-256 digest without interpreting the data.
    /// </summary>
    /// <param name="bytes">The exact bytes.</param>
    /// <returns>The uppercase hexadecimal digest.</returns>
    internal static string Hash(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>
    /// Flushes a temporary file before atomically replacing a document while retaining its last generation.
    /// </summary>
    /// <param name="path">The checked destination.</param>
    /// <param name="bytes">The complete replacement bytes.</param>
    internal static void WriteDurable(string path, byte[] bytes)
    {
        PlainPaths.Full(path);
        var temporary = PlainPaths.Full(path + TemporarySuffix);
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                PlainPaths.Full(path);
                PlainPaths.Full(temporary);
                if (attempt > 0 && !ReadBytes(temporary).AsSpan().SequenceEqual(bytes))
                    throw new InvalidDataException(UpdaterText.TransactionStorageAStagedOrBackupFileFailedContentVerification);

                if (File.Exists(path))
                {
                    File.Replace(temporary, path, PlainPaths.Full(path + PreviousSuffix));
                }
                else
                {
                    File.Move(temporary, path);
                }

                return;
            }
            catch (Exception error) when (attempt < _contentionRetries && IsFileContention(error))
            {
                Thread.Sleep(_contentionRetryMilliseconds);
            }
        }
    }

    /// <summary>
    /// Retries only Windows errors whose failed operation retains its source names and bytes.
    /// </summary>
    /// <remarks>
    /// Other ReplaceFile errors can leave different name mappings and must retain the recovery blocker instead of being replayed.
    /// </remarks>
    /// <param name="error">The failed ordinary filesystem operation.</param>
    /// <returns>Whether a bounded retry can revalidate and repeat this operation.</returns>
    private static bool IsFileContention(Exception error)
        => OperatingSystem.IsWindows() && (error is IOException or UnauthorizedAccessException) && (error.HResult & _win32ErrorMask) is _accessDenied or _sharingViolation or _lockViolation or _unableToRemoveReplaced;

    /// <summary>
    /// Promotes a verified ordinary tree, retrying short Windows contention without accepting changed source contents.
    /// </summary>
    /// <remarks>
    /// The caller verifies the first attempt's complete tree. Every retry rechecks that authenticated snapshot before moving, and permanent contention still fails with recovery data retained.
    /// </remarks>
    /// <param name="source">The checked tree whose initial snapshot is already verified.</param>
    /// <param name="destination">The checked absent destination on the same volume.</param>
    /// <param name="expected">The authenticated source entries, relative to its root and ordered by path.</param>
    /// <param name="cancellationToken">The preparation or non-cancellable rollback token.</param>
    /// <returns>Completion after the same-volume move, or a retained filesystem or verification failure.</returns>
    internal static async Task MoveTreeAsync(string source, string destination, LocalFileEntry[] expected, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                PlainPaths.Full(source);
                PlainPaths.Full(destination);
                using var sourceAncestors = OperatingSystem.IsWindows() ? WindowsUpdateAccess.HoldAncestors(source) : null;
                using var destinationAncestors = OperatingSystem.IsWindows() ? WindowsUpdateAccess.HoldAncestors(destination) : null;
                if (attempt > 0 && !(await TreeAsync(source, cancellationToken).ConfigureAwait(false)).SequenceEqual(expected))
                    throw new InvalidDataException(UpdaterText.TransactionStorageGitMetadataChangedAfterPreparation);

                Directory.Move(source, destination);
                return;
            }
            catch (Exception error) when (attempt < _contentionRetries && IsFileContention(error))
            {
                await Task.Delay(_contentionRetryMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Writes a checksummed journal; its checksum detects damage but does not provide authorization.
    /// </summary>
    /// <param name="directory">The transaction directory.</param>
    /// <param name="journal">The next complete generation.</param>
    internal static void WriteJournal(string directory, TransactionJournal journal)
    {
        var payload = Serialize(journal);
        WriteDurable(PlainPaths.Child(directory, JournalFile), Serialize(new JournalEnvelope(Convert.ToBase64String(payload), Hash(payload))));
    }

    /// <summary>
    /// Loads the latest journal, failing closed if it is damaged instead of guessing whether a missing intent executed.
    /// </summary>
    /// <remarks>
    /// The previous generation remains available for diagnosis. Automatic fallback cannot safely infer a mutation whose intent existed only in the damaged generation.
    /// </remarks>
    /// <param name="directory">The checked transaction directory.</param>
    /// <returns>The checksummed progress record.</returns>
    internal static TransactionJournal ReadJournal(string directory)
    {
        var envelope = Deserialize<JournalEnvelope>(ReadBytes(PlainPaths.Child(directory, JournalFile)));
        var payload = Convert.FromBase64String(envelope.Payload);
        if (Hash(payload) != envelope.Sha256)
            throw new InvalidDataException(UpdaterText.TransactionStorageTheTransactionJournalIsDamagedBackupsHaveBeenRetained);

        return Deserialize<TransactionJournal>(payload);
    }

    /// <summary>
    /// Hashes one regular file with no concurrent writers or path redirection.
    /// </summary>
    /// <param name="path">The checked file.</param>
    /// <param name="cancellationToken">The inspection token.</param>
    /// <returns>The exact content identity.</returns>
    internal static async Task<GameFileContent> ContentAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = OpenRead(path);
        return new GameFileContent(stream.Length, Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    /// Makes an owned extracted file durable and verifies its bytes without creating another copy.
    /// </summary>
    /// <param name="path">The owned regular file on the installation volume.</param>
    /// <param name="expected">The independently authorized content.</param>
    /// <param name="cancellationToken">The verification token.</param>
    internal static async Task FlushContentAsync(string path, GameFileContent expected, CancellationToken cancellationToken)
    {
        await using var stream = OperatingSystem.IsWindows() ? WindowsUpdateAccess.OpenRegular(path, writable: true) : new FileStream(PlainPaths.Full(path), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (stream.Length != expected.Length)
            throw new InvalidDataException(UpdaterText.TransactionStorageAStagedOrBackupFileHasAnUnexpectedSize);

        stream.Flush(true);
        if (Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)) != expected.Sha256)
            throw new InvalidDataException(UpdaterText.TransactionStorageAStagedOrBackupFileFailedContentVerification);
    }

    /// <summary>
    /// Copies and flushes exact bytes, rejecting both source and destination hash mismatches.
    /// </summary>
    /// <param name="source">The regular source file.</param>
    /// <param name="destination">The transaction-owned output file.</param>
    /// <param name="expected">The required content identity.</param>
    /// <param name="cancellationToken">The copy token.</param>
    internal static async Task CopyAsync(string source, string destination, GameFileContent expected, CancellationToken cancellationToken)
    {
        PlainPaths.Full(source);
        PlainPaths.Full(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using (var input = OpenRead(source))
        await using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (input.Length != expected.Length)
                throw new InvalidDataException(UpdaterText.TransactionStorageAStagedOrBackupFileHasAnUnexpectedSize);

            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            output.Flush(true);
        }

        if (await ContentAsync(destination, cancellationToken).ConfigureAwait(false) != expected)
            throw new InvalidDataException(UpdaterText.TransactionStorageAStagedOrBackupFileFailedContentVerification);
    }

    /// <summary>
    /// Uses a no-follow Windows handle for reads that can cross the administrator boundary.
    /// </summary>
    /// <param name="path">The validated source file.</param>
    /// <returns>A stable ordinary-file stream.</returns>
    private static FileStream OpenRead(string path)
        => OperatingSystem.IsWindows() ? WindowsUpdateAccess.OpenRegular(path) : new FileStream(PlainPaths.Full(path), FileMode.Open, FileAccess.Read, FileShare.Read);

    /// <summary>
    /// Takes a complete ordinary metadata-tree snapshot including empty directories.
    /// </summary>
    /// <param name="root">The existing metadata directory.</param>
    /// <param name="cancellationToken">The inspection token.</param>
    /// <param name="stage">The kind of recovery data being verified.</param>
    /// <returns>The sorted file and directory entries.</returns>
    internal static async Task<LocalFileEntry[]> TreeAsync(string root, CancellationToken cancellationToken, InstallationStage stage = InstallationStage.VerifyingMetadata)
    {
        PlainPaths.Full(root);
        var entries = new List<LocalFileEntry>();
        var files = new List<(int Index, string Path)>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                PlainPaths.Child(root, relative);
                if (entries.Count >= MaximumTreeEntries)
                    throw new InvalidDataException(UpdaterText.TransactionStorageTheMetadataTreeIsTooLarge);

                if (Directory.Exists(path))
                {
                    entries.Add(new LocalFileEntry(relative, null));
                    pending.Push(path);
                }
                else
                {
                    files.Add((entries.Count, path));
                    entries.Add(new LocalFileEntry(relative, null));
                }
            }
        }

        var result = entries.ToArray();
        var verified = 0;
        var progressGate = new Lock();
        InstallationProgressScope.Report(new(stage, 0, files.Count));
        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = FileConcurrency, CancellationToken = cancellationToken }, async (file, token) =>
        {
            result[file.Index] = result[file.Index] with { Content = await ContentAsync(file.Path, token).ConfigureAwait(false) };
            lock (progressGate)
                InstallationProgressScope.Report(new(stage, ++verified, files.Count));
        }).ConfigureAwait(false);
        return [.. result.OrderBy(entry => entry.Path, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Copies every metadata entry and verifies the entire result before it can be promoted or used for rollback.
    /// </summary>
    /// <param name="source">The complete source tree.</param>
    /// <param name="destination">The transaction-owned destination tree.</param>
    /// <param name="expected">The authenticated complete tree snapshot.</param>
    /// <param name="cancellationToken">The copy token.</param>
    internal static async Task CopyTreeAsync(string source, string destination, LocalFileEntry[] expected, CancellationToken cancellationToken)
    {
        if (!(await TreeAsync(source, cancellationToken).ConfigureAwait(false)).SequenceEqual(expected))
            throw new InvalidDataException(UpdaterText.TransactionStorageGitMetadataChangedAfterPreparation);

        Directory.CreateDirectory(PlainPaths.Full(destination));
        await Parallel.ForEachAsync(expected, new ParallelOptions { MaxDegreeOfParallelism = FileConcurrency, CancellationToken = cancellationToken }, async (entry, token) =>
        {
            var target = PlainPaths.Child(destination, entry.Path);
            if (entry.Content is null)
            {
                Directory.CreateDirectory(target);
            }
            else
            {
                await CopyAsync(PlainPaths.Child(source, entry.Path), target, entry.Content, token).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        if (!(await TreeAsync(destination, cancellationToken).ConfigureAwait(false)).SequenceEqual(expected))
            throw new InvalidDataException(UpdaterText.TransactionStorageTheCopiedGitMetadataFailedVerification);
    }

    /// <summary>
    /// Wraps a journal generation with an accidental-corruption checksum.
    /// </summary>
    /// <remarks>
    /// Constructs the storage envelope; neither field is a trust root.
    /// </remarks>
    /// <param name="Payload">The base64-encoded exact journal bytes.</param>
    /// <param name="Sha256">Their digest.</param>
    private sealed record JournalEnvelope(string Payload, string Sha256);
}
