namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportDiagnosticsLayerCount
{
    public string FeatureType { get; set; } = string.Empty;

    public string? Category { get; set; }

    public int Count { get; set; }
}
