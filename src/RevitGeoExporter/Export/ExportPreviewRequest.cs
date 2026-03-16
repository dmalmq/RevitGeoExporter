using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Core.Schema;
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
        Point2D? surveyPointSharedCoordinates,
        UnitSource unitSource,
        string roomCategoryParameterName,
        LinkExportOptions? linkExportOptions,
        SchemaProfile? activeSchemaProfile,
        string? previewBasemapUrlTemplate,
        string? previewBasemapAttribution)
    {
        string normalizedSourceCoordinateSystemId = sourceCoordinateSystemId?.Trim() ?? string.Empty;
        string normalizedSourceCoordinateSystemDefinition = sourceCoordinateSystemDefinition?.Trim() ?? string.Empty;
        string normalizedRoomCategoryParameterName = roomCategoryParameterName?.Trim() ?? string.Empty;
        string normalizedPreviewBasemapUrlTemplate = previewBasemapUrlTemplate?.Trim() ?? string.Empty;
        string normalizedPreviewBasemapAttribution = previewBasemapAttribution?.Trim() ?? string.Empty;
        GeometryRepairOptions normalizedGeometryRepairOptions = geometryRepairOptions ?? throw new ArgumentNullException(nameof(geometryRepairOptions));

        if (normalizedRoomCategoryParameterName.Length == 0)
        {
            normalizedRoomCategoryParameterName = "Name";
        }

        if (normalizedPreviewBasemapUrlTemplate.Length == 0)
        {
            normalizedPreviewBasemapUrlTemplate = PreviewBasemapSettings.DefaultUrlTemplate;
        }

        if (normalizedPreviewBasemapAttribution.Length == 0)
        {
            normalizedPreviewBasemapAttribution = PreviewBasemapSettings.DefaultAttribution;
        }

        SelectedViews = selectedViews ?? throw new ArgumentNullException(nameof(selectedViews));
        FeatureTypes = featureTypes;
        GeometryRepairOptions = normalizedGeometryRepairOptions.Clone();
        UiLanguage = uiLanguage;
        CoordinateMode = coordinateMode;
        TargetEpsg = targetEpsg;
        SourceEpsg = sourceEpsg;
        SourceCoordinateSystemId = normalizedSourceCoordinateSystemId;
        SourceCoordinateSystemDefinition = normalizedSourceCoordinateSystemDefinition;
        SurveyPointSharedCoordinates = surveyPointSharedCoordinates;
        UnitSource = unitSource;
        RoomCategoryParameterName = normalizedRoomCategoryParameterName;
        LinkExportOptions = linkExportOptions?.Clone() ?? new LinkExportOptions();
        ActiveSchemaProfile = activeSchemaProfile?.Clone() ?? SchemaProfile.CreateCoreProfile();
        PreviewBasemapUrlTemplate = normalizedPreviewBasemapUrlTemplate;
        PreviewBasemapAttribution = normalizedPreviewBasemapAttribution;
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

    public Point2D? SurveyPointSharedCoordinates { get; }

    public UnitSource UnitSource { get; }

    public string RoomCategoryParameterName { get; }

    public LinkExportOptions LinkExportOptions { get; }

    public SchemaProfile ActiveSchemaProfile { get; }

    public string PreviewBasemapUrlTemplate { get; }

    public string PreviewBasemapAttribution { get; }
}
