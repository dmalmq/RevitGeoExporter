namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportDiagnosticsFloorOverride
{
    public string FloorTypeName { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public int Count { get; set; }
}
