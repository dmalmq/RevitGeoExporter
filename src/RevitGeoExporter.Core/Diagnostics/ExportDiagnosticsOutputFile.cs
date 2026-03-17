using System.Collections.Generic;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportDiagnosticsOutputFile
{
    public string ViewName { get; set; } = string.Empty;

    public long ViewId { get; set; }

    public string FeatureType { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public int FeatureCount { get; set; }

    public string ArtifactKey { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string PackagingMode { get; set; } = string.Empty;

    public string Disposition { get; set; } = string.Empty;

    public List<long> ContributingViewIds { get; set; } = new();

    public List<string> ContributingViewNames { get; set; } = new();

    public List<string> ContributingLevelNames { get; set; } = new();

    public List<string> LayerNames { get; set; } = new();
}
