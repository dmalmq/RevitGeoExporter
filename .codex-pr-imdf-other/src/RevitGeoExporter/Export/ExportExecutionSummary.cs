namespace RevitGeoExporter.Export;

public sealed class ExportExecutionSummary
{
    public IncrementalExportMode IncrementalExportMode { get; set; }

    public int ChangedViewCount { get; set; }

    public int ReusedViewCount { get; set; }

    public int MissingBaselineArtifactCount { get; set; }

    public string? FullRewriteReason { get; set; }
}
