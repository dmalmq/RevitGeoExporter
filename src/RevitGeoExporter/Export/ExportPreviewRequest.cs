using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.UI;

namespace RevitGeoExporter.Export;

public sealed class ExportPreviewRequest
{
    public ExportPreviewRequest(
        IReadOnlyList<ViewPlan> selectedViews,
        ExportFeatureType featureTypes,
        GeometryRepairOptions geometryRepairOptions,
        UiLanguage uiLanguage)
    {
        SelectedViews = selectedViews ?? throw new ArgumentNullException(nameof(selectedViews));
        FeatureTypes = featureTypes;
        GeometryRepairOptions = geometryRepairOptions?.Clone() ?? throw new ArgumentNullException(nameof(geometryRepairOptions));
        UiLanguage = uiLanguage;
    }

    public IReadOnlyList<ViewPlan> SelectedViews { get; }

    public ExportFeatureType FeatureTypes { get; }

    public GeometryRepairOptions GeometryRepairOptions { get; }

    public UiLanguage UiLanguage { get; }
}
