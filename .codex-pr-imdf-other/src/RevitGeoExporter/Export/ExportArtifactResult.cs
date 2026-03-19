using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoExporter.Export;

public sealed class ExportArtifactResult
{
    public ExportArtifactResult(
        string artifactKey,
        string artifactName,
        string outputFilePath,
        PackagingMode packagingMode,
        ArtifactDisposition disposition,
        IReadOnlyList<long> contributingViewIds,
        IReadOnlyList<string> contributingViewNames,
        IReadOnlyList<string> contributingLevelNames,
        IReadOnlyList<string> layerNames,
        int featureCount)
    {
        ArtifactKey = string.IsNullOrWhiteSpace(artifactKey)
            ? throw new ArgumentException("An artifact key is required.", nameof(artifactKey))
            : artifactKey.Trim();
        ArtifactName = string.IsNullOrWhiteSpace(artifactName)
            ? throw new ArgumentException("An artifact name is required.", nameof(artifactName))
            : artifactName.Trim();
        OutputFilePath = string.IsNullOrWhiteSpace(outputFilePath)
            ? throw new ArgumentException("An output file path is required.", nameof(outputFilePath))
            : outputFilePath.Trim();
        PackagingMode = packagingMode;
        Disposition = disposition;
        ContributingViewIds = contributingViewIds ?? throw new ArgumentNullException(nameof(contributingViewIds));
        ContributingViewNames = contributingViewNames ?? throw new ArgumentNullException(nameof(contributingViewNames));
        ContributingLevelNames = contributingLevelNames ?? throw new ArgumentNullException(nameof(contributingLevelNames));
        LayerNames = layerNames ?? throw new ArgumentNullException(nameof(layerNames));
        FeatureCount = featureCount;
    }

    public string ArtifactKey { get; }

    public string ArtifactName { get; }

    public string OutputFilePath { get; }

    public PackagingMode PackagingMode { get; }

    public ArtifactDisposition Disposition { get; }

    public IReadOnlyList<long> ContributingViewIds { get; }

    public IReadOnlyList<string> ContributingViewNames { get; }

    public IReadOnlyList<string> ContributingLevelNames { get; }

    public IReadOnlyList<string> LayerNames { get; }

    public int FeatureCount { get; }

    public string ViewSummary => string.Join(", ", ContributingViewNames.Distinct(StringComparer.Ordinal));

    public string LayerSummary => string.Join(", ", LayerNames.Distinct(StringComparer.OrdinalIgnoreCase));
}

public enum ArtifactDisposition
{
    Written = 0,
    ReusedFromBaseline = 1,
}
