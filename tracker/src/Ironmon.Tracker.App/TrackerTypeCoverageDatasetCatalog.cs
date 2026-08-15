using System.Reflection;

namespace Ironmon.Tracker.App;

/// <summary>
/// Loads the release-precalculated defensive type populations bundled with the tracker.
/// </summary>
internal static class TrackerTypeCoverageDatasetCatalog
{
    private const string _datasetResourceName = "Ironmon.Tracker.App.Resources.Coverage.type_coverage.json";

    /// <summary>
    /// Loads and validates the bundled aggregate coverage dataset.
    /// </summary>
    /// <returns>The validated release dataset.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the embedded resource is missing.</exception>
    /// <exception cref="InvalidDataException">Thrown when the embedded dataset violates its population invariants.</exception>
    internal static TypeCoverageDataset Load()
    {
        Assembly assembly = typeof(TrackerTypeCoverageDatasetCatalog).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(_datasetResourceName)
            ?? throw new InvalidOperationException("The bundled type coverage dataset is missing.");

        return TypeCoverageDatasetJson.Deserialize(stream);
    }
}
