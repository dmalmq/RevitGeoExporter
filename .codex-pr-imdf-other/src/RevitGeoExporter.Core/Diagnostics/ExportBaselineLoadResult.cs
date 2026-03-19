using System.Collections.Generic;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportBaselineLoadResult
{
    public ExportDiagnosticsReport? Report { get; set; }

    public ExportPackageManifest? Manifest { get; set; }

    public ExportBaselineSnapshot? Snapshot { get; set; }

    public IReadOnlyList<string> Warnings { get; set; } = new List<string>();
}
