using System.Text.Json;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.ReleaseTool;

/// <summary>
/// Imports the last pre-updater release from its frozen candidate and independently generated historical game tree.
/// </summary>
internal static class LegacyHistory
{
    internal const string ImportName = "legacy-import.json";
    private const string CandidateName = "candidate.json";
    private const string SchemaProperty = "schema_version";
    private const string RepositoryProperty = "repository";
    private const string VersionProperty = "version";
    private const string InputsProperty = "inputs";
    private const string CommitProperty = "game_commit";
    private const string AssetsProperty = "assets";
    private const string NameProperty = "name";
    private const string BytesProperty = "bytes";
    private const string HashProperty = "sha256";

    /// <summary>
    /// Checks old package hashes against published candidate evidence before granting any legacy ownership.
    /// </summary>
    /// <param name="directory">The GitHub-restored historical candidate and exact game inventory.</param>
    /// <returns>The explicit legacy inventories that the new release will authenticate.</returns>
    internal static (string Version, ReleaseFileInventory Game, ReleaseFileInventory[] Packages) Read(string directory)
    {
        var request = ReleaseJson.Parse<LegacyImport>(File.ReadAllBytes(PlainPaths.Child(directory, ImportName)), ReleaseJson.ManifestLimit);
        ReleaseProtocol.ParseVersion(request.Version);
        ReleaseProtocol.ParseVersion(request.GameVersion);
        var bytes = File.ReadAllBytes(PlainPaths.Child(directory, CandidateName));
        ReleaseJson.Validate(bytes, ReleaseJson.ManifestLimit);
        using var candidate = JsonDocument.Parse(bytes);
        var root = candidate.RootElement;
        if (root.GetProperty(SchemaProperty).GetInt32() != 1 || root.GetProperty(RepositoryProperty).GetString() != ReleaseProtocol.Repository)
            throw new InvalidDataException("The historical candidate has an unsupported schema or repository.");

        using var compressed = File.OpenRead(PlainPaths.Child(directory, request.GameInventory));
        using var manifest = File.OpenRead(PlainPaths.Child(directory, request.GameManifest));
        var game = HoennGameBaseline.Load(compressed, manifest);
        if (root.GetProperty(VersionProperty).GetString() != request.Version || root.GetProperty(InputsProperty).GetProperty(CommitProperty).GetString() != game.Commit)
            throw new InvalidDataException("The historical game inventory and published candidate disagree.");

        var packages = new List<ReleaseFileInventory>();
        foreach (var flavor in new[] { ReleaseProtocol.SelfContained, ReleaseProtocol.RuntimeRequired })
        {
            var name = ReleaseBundle.PackageName(request.Version, flavor);
            var asset = root.GetProperty(AssetsProperty).EnumerateArray().Single(item => item.GetProperty(NameProperty).GetString() == name);
            var actual = ReleaseBundle.Asset(directory, request.Version, name, ReleaseProtocol.TrackerRolePrefix + flavor);
            if (actual.Bytes != asset.GetProperty(BytesProperty).GetInt64() || actual.Sha256 != asset.GetProperty(HashProperty).GetString())
                throw new InvalidDataException("A legacy package differs from its published frozen candidate.");

            packages.Add(new ReleaseFileInventory(ReleaseProtocol.InventoryDocument, 1, ReleaseProtocol.IronmonScope, request.Version, game.Commit, flavor, BundleFiles.Archive(PlainPaths.Child(directory, name))));
        }

        var inventory = ReleaseBundle.GameInventory(game, request.GameVersion);
        return (request.Version, inventory, [.. packages]);
    }
}

/// <summary>
/// Identifies the verified inputs for the first updater-enabled release's legacy bridge.
/// </summary>
/// <remarks>
/// Contains only public metadata restored by the candidate job; no installed player's files are inventoried.
/// </remarks>
/// <param name="Version">The previous published version.</param>
/// <param name="GameVersion">Its exact game's display version.</param>
/// <param name="GameInventory">The flat generated historical inventory basename.</param>
/// <param name="GameManifest">Its companion checksum basename.</param>
internal sealed record LegacyImport(string Version, string GameVersion, string GameInventory, string GameManifest);
