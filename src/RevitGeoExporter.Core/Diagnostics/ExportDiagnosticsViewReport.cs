using System.Collections.Generic;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportDiagnosticsViewReport
{
    public long ViewId { get; set; }

    public string ViewName { get; set; } = string.Empty;

    public string LevelName { get; set; } = string.Empty;

    public List<ExportDiagnosticsLayerCount> Layers { get; set; } = new();

    public List<ExportDiagnosticsFamilyOccurrence> UnsupportedOpeningFamilies { get; set; } = new();

    public List<ExportDiagnosticsFloorOverride> AppliedFloorOverrides { get; set; } = new();

    public List<ExportDiagnosticsUnassignedFloorGroup> UnassignedFloorTypes { get; set; } = new();

    public int UnsnappedOpeningCount { get; set; }
}
