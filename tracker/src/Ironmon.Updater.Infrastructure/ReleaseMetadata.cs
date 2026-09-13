using Ironmon.Updater.Core;
namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Reads individually authenticated inventories and notes from one bounded metadata download.
/// </summary>
internal static class ReleaseMetadata
{
    internal const string Name = "update-data.json";
    internal const string Role = "update-data";
    internal const int Limit = 48 * 1024 * 1024;
    internal const string DocumentType = "release-data";

    /// <summary>
    /// Verifies both the shared container and selected document against signed identities.
    /// </summary>
    /// <param name="bytes">The complete downloaded container.</param>
    /// <param name="asset">The independently authenticated logical asset.</param>
    /// <returns>The exact original document bytes.</returns>
    internal static byte[] Read(byte[] bytes, ReleaseAsset asset)
    {
        var container = asset.Container ?? throw new InvalidDataException(UpdaterText.ReleaseMetadataTheAssetHasNoMetadataContainer);
        return Read(Parse(bytes, container), asset);
    }

    /// <summary>
    /// Authenticates and parses a shared container once for reuse across its referenced documents.
    /// </summary>
    /// <param name="bytes">The complete container bytes.</param>
    /// <param name="container">The independently signed container identity.</param>
    /// <returns>The validated document container.</returns>
    internal static ReleaseMetadataDocument Parse(byte[] bytes, ReleaseContainer container)
    {
        if (container.Name != Name || container.Bytes is <= 0 or > Limit || bytes.LongLength != container.Bytes || TransactionStorage.Hash(bytes) != ReleaseProtocol.Content(container.Bytes, container.Sha256).Sha256)
            throw new InvalidDataException(UpdaterText.ReleaseMetadataTheMetadataContainerDiffersFromItsSignedIdentity);

        var document = ReleaseJson.Parse<ReleaseMetadataDocument>(bytes, Limit);
        if (document.DocumentType != DocumentType || document.SchemaVersion != 1 || document.Files.Length is < 1 or > 256 || document.Files.Select(file => file.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != document.Files.Length || document.Files.Sum(file => file.Bytes.LongLength) > ReleaseJson.InventoryLimit + ReleaseJson.ManifestLimit)
            throw new InvalidDataException(UpdaterText.ReleaseMetadataTheMetadataContainerIsUnsupportedAmbiguousOrOversized);

        foreach (var file in document.Files)
            ReleaseProtocol.ValidateAssetName(file.Name);

        return document;
    }

    /// <summary>
    /// Rechecks a selected document while reusing an already authenticated container parse.
    /// </summary>
    /// <param name="document">The container authenticated against this asset's container identity.</param>
    /// <param name="asset">The independently signed logical document.</param>
    /// <returns>The original document bytes.</returns>
    internal static byte[] Read(ReleaseMetadataDocument document, ReleaseAsset asset)
    {
        var selected = document.Files.SingleOrDefault(file => file.Name == asset.Name) ?? throw new InvalidDataException(UpdaterText.ReleaseMetadataTheSignedDocumentIsMissingFromItsMetadataContainer);
        if (selected.Bytes.LongLength != asset.Bytes || TransactionStorage.Hash(selected.Bytes) != ReleaseProtocol.Content(asset.Bytes, asset.Sha256).Sha256)
            throw new InvalidDataException(UpdaterText.ReleaseMetadataAnEmbeddedDocumentDiffersFromItsSignedIdentity);

        return selected.Bytes;
    }
}

/// <summary>
/// Carries the complete original document bytes without publishing each document separately.
/// </summary>
/// <remarks>
/// Constructs a bounded transport document whose identity is authenticated by the release manifest.
/// </remarks>
/// <param name="DocumentType">The fixed container discriminator.</param>
/// <param name="SchemaVersion">The supported container schema.</param>
/// <param name="Files">The uniquely named original documents.</param>
internal sealed record ReleaseMetadataDocument(string DocumentType, int SchemaVersion, ReleaseMetadataFile[] Files);

/// <summary>
/// Preserves one historical inventory or release note exactly across publications.
/// </summary>
/// <remarks>
/// Constructs an embedded document whose length and digest remain independently signed.
/// </remarks>
/// <param name="Name">The logical asset basename.</param>
/// <param name="Bytes">The original document bytes.</param>
internal sealed record ReleaseMetadataFile(string Name, byte[] Bytes);
