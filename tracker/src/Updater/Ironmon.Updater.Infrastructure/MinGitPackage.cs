namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Pins a reviewed Git archive instead of discovering the latest executable at runtime.
/// </summary>
/// <remarks>
/// Initializes the archive identity used for size and digest verification; callers must supply a trusted pin.
/// </remarks>
/// <param name="Source">The exact archive URL.</param>
/// <param name="Size">The expected compressed size.</param>
/// <param name="Sha256">The expected archive SHA-256.</param>
public sealed record MinGitPackage(Uri Source, long Size, string Sha256)
{
    /// <summary>
    /// Gets the reviewed standard Windows x64 MinGit package.
    /// </summary>
    public static MinGitPackage Pinned { get; } = new(new Uri(AssetUrl), AssetSize, AssetHash);

    internal const string Executable = "cmd/git.exe";
    internal const string License = "LICENSE.txt";
    internal const string GitCore = "mingw64/libexec/git-core";
    internal const string MingwBin = "mingw64/bin";
    internal const string UsrBin = "usr/bin";
    internal const string Cmd = "cmd";
    private const string AssetUrl = "https://github.com/git-for-windows/git/releases/download/v2.55.0.windows.5/MinGit-2.55.0.5-64-bit.zip";
    private const long AssetSize = 38989688;
    private const string AssetHash = "56d7b226b7693196cfc71fef26568f536c4a021ab6c37ff2db4287bed908e96e";
}
