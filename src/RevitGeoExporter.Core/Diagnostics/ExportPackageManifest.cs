using System;
using System.Collections.Generic;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportPackageManifest
{
    public string SourceModelName { get; set; } = string.Empty;

    public string SourceDocumentKey { get; set; } = string.Empty;

    public string? ProfileName { get; set; }

    public string SchemaProfileName { get; set; } = string.Empty;

    public string ValidationPolicyProfileName { get; set; } = string.Empty;

    public string OperatorName { get; set; } = string.Empty;

    public string CoordinateMode { get; set; } = string.Empty;

    public int? SourceEpsg { get; set; }

    public string? SourceCoordinateSystemId { get; set; }

    public string? SourceCoordinateSystemDefinition { get; set; }

    public string PackagingMode { get; set; } = string.Empty;

    public string PackageDirectory { get; set; } = string.Empty;

    public int TargetEpsg { get; set; }

    public DateTimeOffset ExportedAtUtc { get; set; }

    public List<ExportLinkedModelInfo> IncludedLinks { get; set; } = new();

    public List<ExportPackageManifestFile> Files { get; set; } = new();

    public PackageValidationResult? ValidationResult { get; set; }
}

public sealed class ExportPackageManifestFile
{
    public string ArtifactKey { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string OutputFilePath { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string PackagingMode { get; set; } = string.Empty;

    public string Disposition { get; set; } = string.Empty;

    public int FeatureCount { get; set; }

    public List<long> ContributingViewIds { get; set; } = new();

    public List<string> ContributingViewNames { get; set; } = new();

    public List<string> ContributingLevelNames { get; set; } = new();

    public List<string> ContainedLayers { get; set; } = new();

    public List<string> MandatoryLayers { get; set; } = new();

    public bool IsArtifact { get; set; }
}
