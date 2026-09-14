namespace Ironmon.ReleaseTool;

/// <summary>
/// Binds release generation to the workflow's already resolved inputs and staged artifacts.
/// </summary>
/// <remarks>
/// Constructs a build request without network resolution or private signing material.
/// </remarks>
/// <param name="Directory">The isolated release output containing completed packages and helper.</param>
/// <param name="Version">The selected source release version.</param>
/// <param name="GameVersion">The selected game display version.</param>
/// <param name="GameCommit">The exact game commit selected by the release workflow.</param>
/// <param name="GameInventory">The generated compressed game inventory.</param>
/// <param name="GameManifest">Its generated identity and checksum companion.</param>
/// <param name="TrustFile">The reviewed public key document used to build every executable.</param>
/// <param name="HistoryDirectory">The previous published metadata, or null for the first release.</param>
/// <param name="GameDownloadBytes">The pipeline-measured compressed game snapshot size, or null for legacy producer fixtures.</param>
internal sealed record BundleRequest(string Directory, string Version, string GameVersion, string GameCommit, string GameInventory, string GameManifest, string TrustFile, string? HistoryDirectory, long? GameDownloadBytes = null);
