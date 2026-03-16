using System.Collections.Generic;

namespace RevitGeoExporter.Core.Diagnostics;

public sealed class ExportChangeSummary
{
    public List<string> Lines { get; set; } = new();

    public bool HasChanges => Lines.Count > 0;
}
