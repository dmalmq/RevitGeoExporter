using System.Collections.Generic;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Export;

namespace RevitGeoExporter.UI;

public sealed class ExportDialogSettings
{
    public string OutputDirectory { get; set; } = string.Empty;

    public int TargetEpsg { get; set; } = ProjectInfo.DefaultTargetEpsg;

    public ExportFeatureType FeatureTypes { get; set; } = ExportFeatureType.All;

    public List<long> SelectedViewIds { get; set; } = new();

    public bool GenerateDiagnosticsReport { get; set; } = true;

    public bool GeneratePackageOutput { get; set; }

    public bool IncludePackageLegend { get; set; } = true;

    public GeometryRepairOptions GeometryRepairOptions { get; set; } = new();

    public UiLanguage UiLanguage { get; set; } = UiLanguage.English;

    public CoordinateExportMode CoordinateMode { get; set; } = CoordinateExportMode.SharedCoordinates;

    public UnitSource UnitSource { get; set; } = UnitSource.Floors;

    public string RoomCategoryParameterName { get; set; } = "Name";

    public LinkExportOptions LinkExportOptions { get; set; } = new();

    public string PreviewBasemapUrlTemplate { get; set; } = PreviewBasemapSettings.DefaultUrlTemplate;

    public string PreviewBasemapAttribution { get; set; } = PreviewBasemapSettings.DefaultAttribution;
}
