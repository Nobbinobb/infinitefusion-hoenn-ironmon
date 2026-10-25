namespace Ironmon.Tracker.Tests.Runtime;

/// <summary>
/// Owns a short native-engine directory while keeping acceptance evidence in its named output directory.
/// </summary>
internal sealed class NativeRuntimeFixture : IDisposable
{
    private const string _engineDirectory = "data/n";
    private const string _idFormat = "N";
    private const int _maximumDirectoryLength = 120;
    private readonly string _parentDirectory;

    /// <summary>
    /// Creates an isolated engine directory below the source checkout without exceeding the bundled engine's path limit.
    /// </summary>
    /// <param name="sourceRoot">The absolute source checkout containing the owned data directory.</param>
    public NativeRuntimeFixture(string sourceRoot)
    {
        _parentDirectory = Path.GetFullPath(Path.Combine(sourceRoot, _engineDirectory));
        DirectoryPath = Path.Combine(_parentDirectory, Guid.NewGuid().ToString(_idFormat));
        if (DirectoryPath.Length > _maximumDirectoryLength)
            throw new InvalidOperationException("The bundled engine needs a shorter native test directory. Move the checkout to a shorter path before running native acceptance tests.");

        Directory.CreateDirectory(DirectoryPath);
    }

    /// <summary>
    /// Gets the absolute directory containing this test's disposable native engine and configuration.
    /// </summary>
    public string DirectoryPath { get; }

    /// <summary>
    /// Removes only this fixture's generated engine directory, preserving the separately retained acceptance evidence.
    /// </summary>
    public void Dispose()
    {
        if (Path.GetDirectoryName(DirectoryPath) != _parentDirectory || !Guid.TryParseExact(Path.GetFileName(DirectoryPath), _idFormat, out _))
            throw new InvalidOperationException("Refusing to remove a native runtime outside its owned fixture directory.");

        if (Directory.Exists(DirectoryPath))
            Directory.Delete(DirectoryPath, recursive: true);
    }
}
