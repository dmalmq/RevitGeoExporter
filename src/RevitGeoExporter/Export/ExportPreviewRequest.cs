using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.UI;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Export;

public sealed class ExportPreviewRequest
{
    public ExportPreviewRequest(
        IReadOnlyList<ViewPlan> selectedViews,
        ExportFeatureType featureTypes,
        GeometryRepairOptions geometryRepairOptions,
        UiLanguage uiLanguage,
        UnitSource unitSource,
        string roomCategoryParameterName)
    {
        SelectedViews = selectedViews ?? throw new ArgumentNullException(nameof(selectedViews));
        FeatureTypes = featureTypes;
        GeometryRepairOptions = geometryRepairOptions?.Clone() ?? throw new ArgumentNullException(nameof(geometryRepairOptions));
        UiLanguage = uiLanguage;
        UnitSource = unitSource;
        RoomCategoryParameterName = string.IsNullOrWhiteSpace(roomCategoryParameterName) ? "Name" : roomCategoryParameterName.Trim();
    }

    public IReadOnlyList<ViewPlan> SelectedViews { get; }

    public ExportFeatureType FeatureTypes { get; }

    public GeometryRepairOptions GeometryRepairOptions { get; }

    public UiLanguage UiLanguage { get; }

    public UnitSource UnitSource { get; }

    public string RoomCategoryParameterName { get; }
}
