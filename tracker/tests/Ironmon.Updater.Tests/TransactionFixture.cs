using Ironmon.Updater.Infrastructure;
using Ironmon.Updater.Core;
using System.Security.Cryptography;
using static Ironmon.Updater.Tests.FilePlannerFixture;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Owns disposable program, payload and Git trees with independently held fixture authorization.
/// </summary>
internal sealed class TransactionFixture : IDisposable, ITransactionAuthority
{
    internal const string OldFile = "Data/old.bin";
    internal const string ChangedFile = "Data/changed.bin";
    internal const string NewFile = "Data/new/added.bin";
    internal const string SaveFile = "File A.rxdata";
    private const string RootName = "installation";
    private const string PayloadName = "payload";
    private const string GitName = "prepared-git";
    private const string GitIndex = ".git/index";
    private const string GitRef = ".git/refs/heads/releases";
    private const string GitObject = ".git/objects/old";
    private const string IndexName = "index";
    private const string RefName = "refs/heads/releases";
    private const string ObjectName = "objects/new";
    internal const string FixtureMarker = ".transaction-fixture";
    private readonly TestWorkspace _workspace = new();
    private byte[]? _authorized;

    /// <summary>
    /// Gets the disposable installation root.
    /// </summary>
    internal string Root => _workspace.PathFor(RootName);

    /// <summary>
    /// Gets the disposable extracted payload root.
    /// </summary>
    internal string Payload => _workspace.PathFor(PayloadName);

    /// <summary>
    /// Gets separately prepared Git metadata.
    /// </summary>
    internal string PreparedGit => _workspace.PathFor(GitName);

    /// <summary>
    /// Gets the exact authorized descriptor after preparation.
    /// </summary>
    internal TransactionDescription Description { get; private set; } = null!;

    /// <summary>
    /// Gets or sets a final component verification failure for rollback fixtures.
    /// </summary>
    internal bool RejectFinal { get; set; }

    /// <summary>
    /// Gets the transaction-owned recovery directory.
    /// </summary>
    internal string DirectoryPath => Path.Combine(Root, InstallationLease.StateDirectory, TransactionStorage.TransactionsDirectory, Description.TransactionId.ToString(TransactionStorage.GuidFormat));

    /// <summary>
    /// Gets the authorized descriptor hash for the isolated crash host.
    /// </summary>
    internal string AuthorizedHash => TransactionStorage.Hash(_authorized!);

    /// <summary>
    /// Writes version A, stages version B and creates verified backups using the real transaction engine.
    /// </summary>
    /// <param name="git">Whether Git metadata participates.</param>
    /// <param name="zip">Whether the original installation lacks Git metadata.</param>
    /// <returns>The prepared transaction engine.</returns>
    internal async Task<UpdateTransaction> PrepareAsync(bool git = false, bool zip = false)
    {
        Write(Root, OldFile, 1);
        Write(Root, ChangedFile, 1);
        Write(Root, SaveFile, 3);
        Write(Root, FixtureMarker, 1);
        Write(Payload, ChangedFile, 2);
        Write(Payload, NewFile, 2);
        if (git)
        {
            if (!zip)
            {
                Write(Root, GitIndex, 1);
                Write(Root, GitRef, 1);
                Write(Root, GitObject, 1);
            }

            Write(PreparedGit, IndexName, 2);
            Write(PreparedGit, RefName, 2);
            Write(PreparedGit, ObjectName, 2);
        }

        var plan = Planner().Create([File(OldFile, 1), File(ChangedFile, 1)], await InstallationFileSnapshot.ReadAsync(Root), [File(ChangedFile, 2), File(NewFile, 2)]);
        Description = await UpdateTransaction.DescribeAsync(Root, plan, git ? PreparedGit : null);
        _authorized = TransactionStorage.Serialize(Description);
        var engine = Engine();
        await engine.PrepareAsync(Description, Payload, git ? PreparedGit : null);
        return engine;
    }

    /// <summary>
    /// Prepares an empty destination using disposable payload ownership or the ordinary copy fallback.
    /// </summary>
    /// <param name="paths">The signed synthetic target files.</param>
    /// <param name="consumePayload">Whether preparation may take ownership of the extraction.</param>
    /// <param name="existing">Whether an unrelated file forces the existing-installation path.</param>
    internal async Task PrepareFreshAsync(string[] paths, bool consumePayload = true, bool existing = false)
    {
        Write(Root, InstallationLease.StateDirectory + '/' + FixtureMarker, 1);
        if (existing)
            Write(Root, FixtureMarker, 1);

        foreach (var path in paths)
            Write(Payload, path, 2);

        var plan = Planner().Create([], await InstallationFileSnapshot.ReadAsync(Root), paths.Select(path => File(path, 2)));
        Description = await UpdateTransaction.DescribeAsync(Root, plan);
        _authorized = TransactionStorage.Serialize(Description);
        if (consumePayload)
        {
            await Engine().PrepareOwnedAsync(Description, Payload, null, CancellationToken.None);
        }
        else
        {
            await Engine().PrepareAsync(Description, Payload);
        }
    }

    /// <summary>
    /// Builds a new engine to prove recovery has no dependence on the original process state.
    /// </summary>
    /// <param name="progress">The optional fault boundary.</param>
    /// <param name="space">The optional simulated free-space provider.</param>
    /// <param name="idle">The optional process-state check.</param>
    /// <returns>The independently reconstructed engine.</returns>
    internal UpdateTransaction Engine(Action<TransactionProgress>? progress = null, Func<string, long>? space = null, Func<CancellationToken, Task>? idle = null)
        => new(this, idle ?? (_ => Task.CompletedTask), progress, space);

    /// <summary>
    /// Accepts only bytes held independently in the fixture, never authority read from the journal.
    /// </summary>
    /// <param name="description">The proposed descriptor.</param>
    /// <param name="exactBytes">Its serialized bytes.</param>
    /// <param name="cancellationToken">The verification token.</param>
    /// <returns>A completed verification or a rejected descriptor.</returns>
    public Task AuthorizeAsync(TransactionDescription description, ReadOnlyMemory<byte> exactBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_authorized is null || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(_authorized), SHA256.HashData(exactBytes.Span)))
            throw new InvalidDataException("Fixture authorization rejected altered transaction bytes.");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Provides an independent component verification gate for commit and failure fixtures.
    /// </summary>
    /// <param name="description">The authenticated descriptor.</param>
    /// <param name="cancellationToken">The verification token.</param>
    /// <returns>The fixture verification result.</returns>
    public Task VerifyInstalledAsync(TransactionDescription description, CancellationToken cancellationToken)
        => RejectFinal ? Task.FromException(new InvalidDataException("Component versions do not match.")) : Task.CompletedTask;

    /// <summary>
    /// Verifies all original files, untouched saves and the absence of a pending writer blocker.
    /// </summary>
    internal async Task AssertRestoredAsync()
    {
        Assert.Equal(Description.Plan.Local, await InstallationFileSnapshot.ReadAsync(Root));
        Assert.False(System.IO.File.Exists(Path.Combine(Root, InstallationLease.StateDirectory, InstallationLease.ActiveFile)));
        Assert.True(System.IO.File.Exists(Path.Combine(DirectoryPath, TransactionStorage.BackupDirectory, ChangedFile)));
    }

    /// <summary>
    /// Writes a small exact fixture payload below its owned root.
    /// </summary>
    /// <param name="root">The fixture owner.</param>
    /// <param name="relative">The relative file path.</param>
    /// <param name="value">The byte value.</param>
    internal static void Write(string root, string relative, byte value)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, [value]);
    }

    /// <summary>
    /// Removes only disposable test-owned trees.
    /// </summary>
    public void Dispose()
        => _workspace.Dispose();
}
