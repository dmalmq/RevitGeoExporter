using System;
using System.Collections.Generic;

namespace RevitGeoExporter.Export;

public sealed class FloorExportPreparationResult
{
    public FloorExportPreparationResult(
        IReadOnlyList<PreparedViewExportData> views,
        IReadOnlyList<string> warnings)
    {
        Views = views ?? throw new ArgumentNullException(nameof(views));
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
    }

    public IReadOnlyList<PreparedViewExportData> Views { get; }

    public IReadOnlyList<string> Warnings { get; }
}
