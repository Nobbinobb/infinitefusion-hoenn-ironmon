using System.IO.Compression;
using System.Security.Cryptography;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.ReleaseTool;

/// <summary>
/// Inventories exact package bytes using the installed updater's path and ownership rules.
/// </summary>
internal static class BundleFiles
{
    internal const string OwnershipPath = "Ironmon Tracker/update-package.json";
    internal const string HelperPath = "Ironmon Tracker/Updater/Ironmon.Updater.exe";
    internal const string TrustPath = "Ironmon Tracker/Updater/trusted-keys.json";
    internal const string NoticesPath = "Ironmon Tracker/Updater/THIRD_PARTY_NOTICES.md";
    private const string Profiles = "Data/Ironmon/generation_profiles/";
    private const string AllFiles = "*";
    private const long MaximumPackageBytes = 8L * 1024 * 1024 * 1024;
    private const int MaximumEntries = 200000;

    /// <summary>
    /// Hashes a stream without retaining whole executables in memory.
    /// </summary>
    /// <param name="stream">The opened artifact or entry.</param>
    /// <returns>The canonical lowercase SHA-256 digest.</returns>
    internal static string Hash(Stream stream)
        => Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

    /// <summary>
    /// Reads a regular package entry with its immutable-profile policy.
    /// </summary>
    /// <param name="path">The normalized relative file path.</param>
    /// <param name="bytes">The declared length.</param>
    /// <param name="stream">The opened file.</param>
    /// <returns>The precise package ownership entry.</returns>
    internal static ReleaseFile Entry(string path, long bytes, Stream stream)
    {
        ReleaseArchive.ValidateRelativePath(path);
        if (bytes is < 0 or > MaximumPackageBytes)
            throw new InvalidDataException("A release entry exceeds the supported size.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long count = 0;
        int read;
        while ((read = stream.Read(buffer)) != 0)
        {
            count = checked(count + read);
            if (count > bytes)
                throw new InvalidDataException("An archive entry expands beyond its declared length.");

            hash.AppendData(buffer, 0, read);
        }

        if (count != bytes)
            throw new InvalidDataException("An archive entry is shorter than its declared length.");

        return new ReleaseFile(path, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), bytes, ReleaseProtocol.IronmonScope, path.StartsWith(Profiles, StringComparison.Ordinal) ? ReleaseProtocol.RetainPolicy : ReleaseProtocol.ReplacePolicy);
    }

    /// <summary>
    /// Inventories every file in an archive and rejects unsafe or nonregular entries.
    /// </summary>
    /// <param name="path">The exact completed archive.</param>
    /// <returns>The ordered package entries.</returns>
    internal static ReleaseFile[] Archive(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Count > MaximumEntries || zip.Entries.Sum(entry => entry.Length) > MaximumPackageBytes)
            throw new InvalidDataException("A release archive exceeds the installed clients' package limits.");

        var files = new List<ReleaseFile>();
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/'))
                throw new InvalidDataException("Release archives must contain regular files only.");

            var type = (entry.ExternalAttributes >> 16) & 0xF000;
            if (type is not (0 or 0x8000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Release archives cannot contain links.");

            using var stream = entry.Open();
            files.Add(Entry(entry.FullName, entry.Length, stream));
        }

        Validate(files);
        return [.. files.OrderBy(file => file.Path, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Applies the same target ownership and duplicate-path constraints as installation.
    /// </summary>
    /// <param name="files">The complete package inventory.</param>
    internal static void Validate(IEnumerable<ReleaseFile> files)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
            if (!paths.Add(file.Path))
                throw new InvalidDataException("A release contains duplicate paths.");

        foreach (var path in paths)
        {
            var parent = path;
            while (parent.LastIndexOf('/') is var index && index >= 0)
            {
                parent = parent[..index];
                if (paths.Contains(parent))
                    throw new InvalidDataException("A release contains conflicting file and directory paths.");
            }
        }

        var managed = files.Where(file => !ReleaseVerifier.IsLegacyDocument(file.Path)).Select(file => new ManagedFile(file.Path, ReleaseProtocol.Content(file.Bytes, file.Sha256), ManagedFileOwner.Ironmon, file.Policy == ReleaseProtocol.RetainPolicy ? ManagedFilePolicy.Retain : ManagedFilePolicy.Replace));
        _ = new FileUpdatePlanner(new FileManagementPolicy()).Create([], [], managed);
    }

    /// <summary>
    /// Writes the package's inner inventory before archiving; the external signed inventory also hashes this file.
    /// </summary>
    /// <param name="root">The staged player distribution.</param>
    /// <param name="version">The current release version.</param>
    /// <param name="flavor">The deployment flavor.</param>
    /// <param name="commit">The selected game commit.</param>
    internal static void WriteOwnership(string root, string version, string flavor, string commit)
    {
        ReleaseProtocol.ParseVersion(version);
        ReleaseProtocol.ValidateCommit(commit);
        if (flavor is not (ReleaseProtocol.SelfContained or ReleaseProtocol.RuntimeRequired))
            throw new InvalidDataException("Unknown package deployment flavor.");

        root = PlainPaths.Full(root);
        PlainPaths.CheckTree(root);
        var files = new List<ReleaseFile>();
        foreach (var path in Directory.EnumerateFiles(root, AllFiles, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative == OwnershipPath)
                throw new IOException("Package ownership metadata already exists; rebuild the distribution first.");

            using var stream = File.OpenRead(path);
            files.Add(Entry(relative, stream.Length, stream));
        }

        Validate(files);
        var inventory = new ReleaseFileInventory(ReleaseProtocol.InventoryDocument, 1, ReleaseProtocol.IronmonScope, version, commit, flavor, [.. files.OrderBy(file => file.Path, StringComparer.Ordinal)]);
        File.WriteAllBytes(PlainPaths.Child(root, OwnershipPath), ReleaseJson.Serialize(inventory));
    }

    /// <summary>
    /// Reads a small required archive document with the release parser's size limit.
    /// </summary>
    /// <param name="archive">The completed package path.</param>
    /// <param name="name">The required entry path.</param>
    /// <returns>The bounded exact entry bytes.</returns>
    internal static byte[] ReadDocument(string archive, string name)
    {
        using var zip = ZipFile.OpenRead(archive);
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException("A required release document is missing.");
        if (entry.Length > ReleaseJson.InventoryLimit)
            throw new InvalidDataException("A package document is too large.");

        using var stream = entry.Open();
        var bytes = new byte[entry.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            throw new InvalidDataException("A package document expands beyond its declared length.");

        return bytes;
    }
}
