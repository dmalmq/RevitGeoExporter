using System.Collections.Generic;
using RevitGeoExporter.Core.Geometry;

namespace RevitGeoExporter.Export;

public sealed class FloorExportPreparationOptions
{
    public IReadOnlyDictionary<string, string>? FloorCategoryOverrides { get; set; }

    public IReadOnlyDictionary<string, string>? FamilyCategoryOverrides { get; set; }

    public IReadOnlyList<string>? AcceptedOpeningFamilies { get; set; }

    public IReadOnlyList<string>? InitialWarnings { get; set; }

    public GeometryRepairOptions? GeometryRepairOptions { get; set; }

    internal IReadOnlyList<ViewExportContext>? ViewContexts { get; set; }
}
