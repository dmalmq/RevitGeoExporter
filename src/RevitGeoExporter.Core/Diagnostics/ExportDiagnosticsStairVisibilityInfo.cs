namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportDiagnosticsStairVisibilityInfo
{
    public long? SourceElementId { get; set; }

    public string? ExportId { get; set; }

    public string? Source { get; set; }

    public int? EvidenceCount { get; set; }

    public int? CandidateCount { get; set; }

    public bool? MaskApplied { get; set; }

    public string? Warning { get; set; }
}
