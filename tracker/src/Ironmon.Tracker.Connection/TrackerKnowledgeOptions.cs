namespace Ironmon.Tracker.Connection;

/// <summary>
/// Configures tracker-owned run knowledge persistence.
/// </summary>
public sealed class TrackerKnowledgeOptions
{
    /// <summary>
    /// Initializes tracker knowledge persistence options.
    /// </summary>
    /// <param name="rootDirectory">The directory containing tracker-owned data.</param>
    /// <exception cref="ArgumentException">Thrown when the directory is empty.</exception>
    public TrackerKnowledgeOptions(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = rootDirectory;
    }

    /// <summary>
    /// Gets the directory containing tracker-owned data.
    /// </summary>
    public string RootDirectory { get; }
}
