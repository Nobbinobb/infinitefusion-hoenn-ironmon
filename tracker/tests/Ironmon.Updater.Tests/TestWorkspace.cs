using System.IO.Compression;
using System.Security.Cryptography;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Owns one disposable fixture beneath ignored test output, outside all installed game data.
/// </summary>
internal sealed class TestWorkspace : IDisposable
{
    private readonly string _parent;
    private const string FixtureFolder = "../../../../../../data/updater/tests";
    private const string FixturePrefix = "updater space ü-";
    private const int CleanupRetries = 20;
    private const int CleanupRetryMilliseconds = 100;

    /// <summary>
    /// Gets the isolated fixture root.
    /// </summary>
    internal string Root { get; }

    /// <summary>
    /// Creates a uniquely named fixture including spaces and Unicode.
    /// </summary>
    internal TestWorkspace()
    {
        _parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, FixtureFolder));
        Directory.CreateDirectory(_parent);
        Root = Path.Combine(_parent, FixturePrefix + Guid.NewGuid());
        Directory.CreateDirectory(Root);
    }

    /// <summary>
    /// Gets a fixture child path.
    /// </summary>
    /// <param name="name">The trusted relative fixture path to append.</param>
    /// <returns>The combined fixture path; this helper does not validate untrusted input.</returns>
    internal string PathFor(string name)
        => Path.Combine(Root, name);

    /// <summary>
    /// Deletes this fixture's owned directory, allowing bounded retries for native image mappings still being released after process exit.
    /// </summary>
    public void Dispose()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                PlainPaths.DeleteOwned(_parent, Root);
                return;
            }
            catch (Exception error) when (attempt < CleanupRetries && error is (IOException or UnauthorizedAccessException))
            {
                Thread.Sleep(CleanupRetryMilliseconds);
            }
        }
    }
}

/// <summary>
/// Supplies deterministic archive bytes and records acquisition attempts.
/// </summary>
/// <remarks>
/// Initializes an in-memory artifact source.
/// </remarks>
/// <param name="bytes">The bytes supplied to a download.</param>
internal sealed class MemoryArtifact(byte[] bytes) : IArtifactSource
{
    /// <summary>
    /// Gets the number of attempted downloads.
    /// </summary>
    internal int Calls { get; private set; }

    /// <summary>
    /// Gets or sets a callback invoked after writing the fixture bytes and before the final cancellation check.
    /// </summary>
    internal Action? DuringDownload { get; set; }

    /// <summary>
    /// Writes fixture bytes, records the call and invokes the download callback before checking cancellation.
    /// </summary>
    /// <param name="source">The requested URI, deliberately ignored by this in-memory fixture.</param>
    /// <param name="destination">The caller-owned writable stream, which remains open.</param>
    /// <param name="maximumBytes">The requested limit, deliberately ignored so cache validation can be tested independently.</param>
    /// <param name="cancellationToken">The token used to cancel the write or completion.</param>
    /// <returns>A task that completes after the fixture bytes and callback have been processed.</returns>
    public async Task CopyToAsync(Uri source, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        Calls++;
        await destination.WriteAsync(bytes, cancellationToken);
        DuringDownload?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
    }
}

/// <summary>
/// Supplies the already pinned official archive without a network dependency during fixture execution.
/// </summary>
/// <remarks>
/// Initializes a file-backed source.
/// </remarks>
/// <param name="path">The local official ZIP.</param>
internal sealed class FileArtifact(string path) : IArtifactSource
{
    /// <summary>
    /// Copies the provisioned archive without downloading it; the cache verifies the copied bytes.
    /// </summary>
    /// <param name="source">The requested URI, deliberately ignored in favor of the fixture file.</param>
    /// <param name="destination">The caller-owned writable stream, which remains open.</param>
    /// <param name="maximumBytes">The requested limit, deliberately ignored by this local fixture source.</param>
    /// <param name="cancellationToken">The token used to cancel the copy.</param>
    /// <returns>A task that completes when the local archive has been copied.</returns>
    public async Task CopyToAsync(Uri source, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(path);
        await input.CopyToAsync(destination, cancellationToken);
    }
}

/// <summary>
/// Constructs authenticated but synthetic ZIPs for negative boundary tests.
/// </summary>
internal static class TestArchives
{
    internal const string FakeExecutable = "fixture executable";
    internal const string FakeLicense = "fixture license";
    private const string SourceUrl = "https://github.com/fixture/tool.zip";

    /// <summary>
    /// Creates a ZIP and matching reviewed-package input for a fixture.
    /// </summary>
    /// <param name="extraName">An optional entry name used to exercise unsafe paths or collisions.</param>
    /// <returns>The synthetic archive bytes and a package pin containing their exact size and SHA-256 digest.</returns>
    internal static (byte[] Bytes, MinGitPackage Package) Create(string? extraName = null)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, MinGitPackage.Executable, FakeExecutable);
            Write(zip, MinGitPackage.License, FakeLicense);
            if (extraName is not null)
                Write(zip, extraName, FakeExecutable);
        }

        var bytes = output.ToArray();
        return (bytes, new MinGitPackage(new Uri(SourceUrl), bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))));
    }

    /// <summary>
    /// Writes one regular archive entry.
    /// </summary>
    /// <param name="zip">The archive being constructed.</param>
    /// <param name="path">The entry name, which may be intentionally invalid for boundary tests.</param>
    /// <param name="content">The text to write into the entry.</param>
    private static void Write(ZipArchive zip, string path, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open());
        writer.Write(content);
    }
}
