using System.Collections.Generic;

namespace RevitGeoExporter.Export;

public sealed class FloorExportPreparationOptions
{
    public IReadOnlyDictionary<string, string>? FloorCategoryOverrides { get; set; }

    public IReadOnlyList<string>? InitialWarnings { get; set; }

    internal IReadOnlyList<ViewExportContext>? ViewContexts { get; set; }
}
