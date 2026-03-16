namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportDiagnosticsOutputFile
{
    public string ViewName { get; set; } = string.Empty;

    public string FeatureType { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public int FeatureCount { get; set; }
}
