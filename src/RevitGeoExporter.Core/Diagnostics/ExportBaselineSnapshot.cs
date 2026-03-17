using System;
using System.Collections.Generic;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportBaselineSnapshot
{
    public string BaselineKey { get; set; } = string.Empty;

    public string SourceDocumentKey { get; set; } = string.Empty;

    public string SourceModelName { get; set; } = string.Empty;

    public string? ProfileName { get; set; }

    public string ConfigurationFingerprint { get; set; } = string.Empty;

    public DateTimeOffset ExportedAtUtc { get; set; }

    public List<ExportBaselineViewSnapshot> Views { get; set; } = new();

    public List<ExportBaselineArtifactSnapshot> Artifacts { get; set; } = new();
}

public sealed class ExportBaselineViewSnapshot
{
    public long ViewId { get; set; }

    public string ViewName { get; set; } = string.Empty;

    public string LevelName { get; set; } = string.Empty;

    public string ContentFingerprint { get; set; } = string.Empty;

    public List<string> ArtifactKeys { get; set; } = new();
}

public sealed class ExportBaselineArtifactSnapshot
{
    public string ArtifactKey { get; set; } = string.Empty;

    public string OutputFilePath { get; set; } = string.Empty;

    public string PackagingMode { get; set; } = string.Empty;

    public List<long> ContributingViewIds { get; set; } = new();

    public List<string> LayerNames { get; set; } = new();

    public int FeatureCount { get; set; }
}
