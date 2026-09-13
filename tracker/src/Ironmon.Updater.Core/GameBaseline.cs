namespace Ironmon.Updater.Core;

/// <summary>
/// Identifies one approved byte representation of a game file.
/// </summary>
/// <remarks>
/// Initializes a size and SHA-256 pair from a trusted historical inventory.
/// </remarks>
/// <param name="Length">The exact file length in bytes.</param>
/// <param name="Sha256">The uppercase SHA-256 digest of those bytes.</param>
public sealed record GameFileContent(long Length, string Sha256);

/// <summary>
/// Binds an official Git blob to explicitly accepted installation bytes.
/// </summary>
/// <remarks>
/// Initializes a tracked file description; Windows text conversion is accepted only when a separate digest is supplied.
/// </remarks>
/// <param name="Path">The case-sensitive, slash-separated game-relative path.</param>
/// <param name="Mode">The Git regular-file mode.</param>
/// <param name="Blob">The exact Git blob object ID.</param>
/// <param name="Canonical">The original blob's byte fingerprint.</param>
/// <param name="WindowsText">An optional explicitly approved CRLF representation.</param>
public sealed record GameBaselineFile(string Path, string Mode, string Blob, GameFileContent Canonical, GameFileContent? WindowsText);

/// <summary>
/// Describes a complete historical game tree used to recognize ZIP installations.
/// </summary>
/// <remarks>
/// Initializes a trusted baseline. Constructing this record does not authenticate an external inventory.
/// </remarks>
/// <param name="Commit">The exact approved historical commit.</param>
/// <param name="Files">Every regular file in that commit, with no omitted managed files.</param>
public sealed record GameBaseline(string Commit, IReadOnlyList<GameBaselineFile> Files);
