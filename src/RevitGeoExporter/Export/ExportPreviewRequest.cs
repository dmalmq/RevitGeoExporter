using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.UI;

namespace RevitGeoExporter.Export;

public sealed class ExportPreviewRequest
{
    public ExportPreviewRequest(
        IReadOnlyList<ViewPlan> selectedViews,
        ExportFeatureType featureTypes,
        GeometryRepairOptions geometryRepairOptions,
        UiLanguage uiLanguage,
        CoordinateExportMode coordinateMode,
        int targetEpsg,
        int? sourceEpsg,
        string? sourceCoordinateSystemId,
        string? sourceCoordinateSystemDefinition,
        UnitSource unitSource,
        string roomCategoryParameterName,
        string? previewBasemapUrlTemplate,
        string? previewBasemapAttribution)
    {
        SelectedViews = selectedViews ?? throw new ArgumentNullException(nameof(selectedViews));
        FeatureTypes = featureTypes;
        GeometryRepairOptions = geometryRepairOptions?.Clone() ?? throw new ArgumentNullException(nameof(geometryRepairOptions));
        UiLanguage = uiLanguage;
        CoordinateMode = coordinateMode;
        TargetEpsg = targetEpsg;
        SourceEpsg = sourceEpsg;
        SourceCoordinateSystemId = string.IsNullOrWhiteSpace(sourceCoordinateSystemId) ? string.Empty : sourceCoordinateSystemId.Trim();
        SourceCoordinateSystemDefinition = string.IsNullOrWhiteSpace(sourceCoordinateSystemDefinition) ? string.Empty : sourceCoordinateSystemDefinition.Trim();
        UnitSource = unitSource;
        RoomCategoryParameterName = string.IsNullOrWhiteSpace(roomCategoryParameterName) ? "Name" : roomCategoryParameterName.Trim();
        PreviewBasemapUrlTemplate = string.IsNullOrWhiteSpace(previewBasemapUrlTemplate)
            ? PreviewBasemapSettings.DefaultUrlTemplate
            : previewBasemapUrlTemplate.Trim();
        PreviewBasemapAttribution = string.IsNullOrWhiteSpace(previewBasemapAttribution)
            ? PreviewBasemapSettings.DefaultAttribution
            : previewBasemapAttribution.Trim();
    }

    public IReadOnlyList<ViewPlan> SelectedViews { get; }

    public ExportFeatureType FeatureTypes { get; }

    public GeometryRepairOptions GeometryRepairOptions { get; }

    public UiLanguage UiLanguage { get; }

    public CoordinateExportMode CoordinateMode { get; }

    public int TargetEpsg { get; }

    public int? SourceEpsg { get; }

    public string SourceCoordinateSystemId { get; }

    public string SourceCoordinateSystemDefinition { get; }

    public UnitSource UnitSource { get; }

    public string RoomCategoryParameterName { get; }

    public string PreviewBasemapUrlTemplate { get; }

    public string PreviewBasemapAttribution { get; }
}
