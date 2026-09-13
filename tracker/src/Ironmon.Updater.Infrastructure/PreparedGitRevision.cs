namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Owns staged Git objects independently of the installed game's files and metadata.
/// </summary>
public sealed class PreparedGitRevision : IDisposable
{
    private readonly string _parent;
    private readonly string _workspace;

    /// <summary>
    /// Gets the isolated bare object repository.
    /// </summary>
    /// <remarks>
    /// The directory is valid only until this handle is disposed.
    /// </remarks>
    public string ObjectDirectory { get; }

    /// <summary>
    /// Gets the exact validated target commit.
    /// </summary>
    public string Commit { get; }

    /// <summary>
    /// Creates a staging handle whose ownership transfers to the provider's caller.
    /// </summary>
    /// <param name="parent">The updater-owned parent that bounds workspace cleanup.</param>
    /// <param name="objectDirectory">The private workspace containing the bare repository and Git home directory.</param>
    /// <param name="commit">The exact commit validated in the staged repository.</param>
    internal PreparedGitRevision(string parent, string objectDirectory, string commit)
    {
        _parent = parent;
        _workspace = objectDirectory;
        ObjectDirectory = Path.Combine(objectDirectory, PrivateGitProvider.ObjectsName);
        Commit = commit;
    }

    /// <summary>
    /// Removes only the owned staging repository; never removes an installation.
    /// </summary>
    public void Dispose()
        => PlainPaths.DeleteOwned(_parent, _workspace);
}
