namespace Ironmon.Tracker.Connection.Sprites;

/// <summary>
/// Describes the custom sprite sheets missing from one Infinite Fusion installation.
/// </summary>
public sealed class CustomSpriteInstallPlan
{
    /// <summary>
    /// Initializes an immutable plan after the installer has validated the game root,
    /// parsed the installed manifest, and removed sheets that are already available.
    /// </summary>
    /// <param name="gameRoot">The normalized Infinite Fusion installation directory.</param>
    /// <param name="pendingSheets">The distinct manifest-backed sheets still missing locally.</param>
    /// <param name="totalSheetCount">The total number of distinct sheets represented by the manifest.</param>
    internal CustomSpriteInstallPlan(string gameRoot, IReadOnlyList<CustomSpriteSheetTarget> pendingSheets, int totalSheetCount)
    {
        GameRoot = gameRoot;
        PendingSheets = pendingSheets;
        TotalSheetCount = totalSheetCount;
    }

    /// <summary>
    /// Gets the validated Infinite Fusion installation directory.
    /// </summary>
    public string GameRoot { get; }

    /// <summary>
    /// Gets the number of distinct custom sprite sheets represented by the installed manifest.
    /// </summary>
    public int TotalSheetCount { get; }

    /// <summary>
    /// Gets the number of valid sheets already present locally.
    /// </summary>
    public int ExistingSheetCount => TotalSheetCount - PendingSheets.Count;

    /// <summary>
    /// Gets the number of sheets that still need to be downloaded.
    /// </summary>
    public int PendingSheetCount => PendingSheets.Count;

    /// <summary>
    /// Gets the ordered internal download targets retained between review and confirmation.
    /// </summary>
    internal IReadOnlyList<CustomSpriteSheetTarget> PendingSheets { get; }
}

/// <summary>
/// Identifies one official sprite-sheet resource and its validated local destination.
/// </summary>
/// <param name="RelativePath">The slash-separated path relative to the official sheet endpoint.</param>
/// <param name="DestinationPath">The absolute destination inside the Infinite Fusion installation.</param>
internal sealed record CustomSpriteSheetTarget(string RelativePath, string DestinationPath);
