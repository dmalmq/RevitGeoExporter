using System.Collections.Generic;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Core.Schema;
using RevitGeoExporter.Core.Validation;
using RevitGeoExporter.Export;

namespace RevitGeoExporter.UI;

public sealed class ExportDialogSettings
{
    public string OutputDirectory { get; set; } = string.Empty;

    public int TargetEpsg { get; set; } = ProjectInfo.DefaultTargetEpsg;

    public ExportFeatureType FeatureTypes { get; set; } = ExportFeatureType.All;

    public List<long> SelectedViewIds { get; set; } = new();

    public IncrementalExportMode IncrementalExportMode { get; set; } = IncrementalExportMode.AllSelectedViews;

    public bool GenerateDiagnosticsReport { get; set; } = true;

    public bool GeneratePackageOutput { get; set; }

    public bool IncludePackageLegend { get; set; } = true;

    public PackagingMode PackagingMode { get; set; } = PackagingMode.PerViewPerFeatureFiles;

    public bool ValidateAfterWrite { get; set; } = true;

    public bool GenerateQgisArtifacts { get; set; }

    public PostExportActionOptions PostExportActions { get; set; } = new();

    public GeometryRepairOptions GeometryRepairOptions { get; set; } = new();

    public UiLanguage UiLanguage { get; set; } = UiLanguage.English;

    public CoordinateExportMode CoordinateMode { get; set; } = CoordinateExportMode.SharedCoordinates;

    public UnitSource UnitSource { get; set; } = UnitSource.Floors;

    public UnitGeometrySource UnitGeometrySource { get; set; } = UnitGeometrySource.Unset;

    public UnitAttributeSource UnitAttributeSource { get; set; } = UnitAttributeSource.Unset;

    public string RoomCategoryParameterName { get; set; } = "Name";

    public LinkExportOptions LinkExportOptions { get; set; } = new();

    public List<SchemaProfile> SchemaProfiles { get; set; } = new() { SchemaProfile.CreateCoreProfile() };

    public string ActiveSchemaProfileName { get; set; } = SchemaProfile.CoreProfileName;

    public List<ValidationPolicyProfile> ValidationPolicyProfiles { get; set; } = ValidationPolicyProfile.NormalizeProfiles(null);

    public string ActiveValidationPolicyProfileName { get; set; } = ValidationPolicyProfile.RecommendedProfileName;

    public string PreviewBasemapUrlTemplate { get; set; } = PreviewBasemapSettings.DefaultUrlTemplate;

    public string PreviewBasemapAttribution { get; set; } = PreviewBasemapSettings.DefaultAttribution;

    public string QgisExecutablePath { get; set; } = string.Empty;
}
