namespace Ironmon.Tracker.Connection.Sprites;

/// <summary>
/// Describes the custom fusion and base-species sprite sheets eligible for synchronization in one Infinite Fusion installation.
/// </summary>
public sealed class CustomSpriteInstallPlan
{
    /// <summary>
    /// Initializes an immutable plan after the installer has validated the game root,
    /// parsed the installed manifest, and excluded remembered missing remote resources.
    /// </summary>
    /// <param name="gameRoot">The normalized Infinite Fusion installation directory.</param>
    /// <param name="pendingSheets">The distinct manifest-backed sheets to check or download.</param>
    /// <param name="totalSheetCount">The total number of distinct sheets represented by the manifest.</param>
    /// <param name="existingSheetCount">The valid sheets already present locally.</param>
    /// <param name="unavailableSheetCount">The missing sheets excluded because the server previously returned 404.</param>
    internal CustomSpriteInstallPlan(string gameRoot, IReadOnlyList<CustomSpriteSheetTarget> pendingSheets, int totalSheetCount, int existingSheetCount, int unavailableSheetCount = 0)
    {
        GameRoot = gameRoot;
        PendingSheets = pendingSheets;
        TotalSheetCount = totalSheetCount;
        ExistingSheetCount = existingSheetCount;
        UnavailableSheetCount = unavailableSheetCount;
    }

    /// <summary>
    /// Gets the validated Infinite Fusion installation directory.
    /// </summary>
    public string GameRoot { get; }

    /// <summary>
    /// Gets the number of distinct custom sprite sheets represented by the installed manifests.
    /// </summary>
    public int TotalSheetCount { get; }

    /// <summary>
    /// Gets the number of valid sheets already present locally.
    /// </summary>
    public int ExistingSheetCount { get; }

    /// <summary>
    /// Gets the number of missing sheets skipped because their endpoint previously returned HTTP 404.
    /// </summary>
    public int UnavailableSheetCount { get; }

    /// <summary>
    /// Gets the number of sheets that will be checked or downloaded.
    /// </summary>
    public int PendingSheetCount => PendingSheets.Count;

    /// <summary>
    /// Gets the ordered internal synchronization targets retained between review and confirmation.
    /// </summary>
    internal IReadOnlyList<CustomSpriteSheetTarget> PendingSheets { get; }
}

/// <summary>
/// Identifies one official sprite-sheet resource and its validated local state.
/// </summary>
/// <remarks>Initializes one immutable synchronization target.</remarks>
/// <param name="RelativePath">The slash-separated path relative to the official sheet endpoint.</param>
/// <param name="DestinationPath">The absolute destination inside the Infinite Fusion installation.</param>
/// <param name="Head">The internal head identifier that owns the sheet.</param>
/// <param name="Variant">The optional sheet variant suffix.</param>
/// <param name="Kind">The Infinite Fusion sprite-sheet library that owns the resource.</param>
/// <param name="HasValidLocalFile">Whether a valid PNG existed when the plan was created.</param>
internal sealed record CustomSpriteSheetTarget(string RelativePath, string DestinationPath, string Head, string Variant, CustomSpriteSheetKind Kind, bool HasValidLocalFile);

/// <summary>
/// Identifies the two official custom-sprite sheet layouts synchronized by the tracker.
/// </summary>
internal enum CustomSpriteSheetKind
{
    /// <summary>Fusion sheets partitioned by head and sheet variant.</summary>
    Fusion = 0,

    /// <summary>Normal-species sheets containing every alternate sprite cell.</summary>
    Base = 1
}
