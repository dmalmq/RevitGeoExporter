using System.Collections.Generic;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Export;

public sealed class FloorExportPreparationOptions
{
    public IReadOnlyDictionary<string, string>? FloorCategoryOverrides { get; set; }

    public IReadOnlyDictionary<string, string>? RoomCategoryOverrides { get; set; }

    public IReadOnlyDictionary<string, string>? FamilyCategoryOverrides { get; set; }

    public IReadOnlyList<string>? AcceptedOpeningFamilies { get; set; }

    public IReadOnlyList<string>? InitialWarnings { get; set; }

    public GeometryRepairOptions? GeometryRepairOptions { get; set; }

    public UnitSource UnitSource { get; set; } = UnitSource.Floors;

    public string RoomCategoryParameterName { get; set; } = "Name";

    internal IReadOnlyList<ViewExportContext>? ViewContexts { get; set; }
}
