namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportDiagnosticsPhaseTiming
{
    public string PhaseName { get; set; } = string.Empty;

    public long DurationMilliseconds { get; set; }
}
