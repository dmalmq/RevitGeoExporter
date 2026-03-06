using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoExporter.UI;

namespace RevitGeoExporter.Export;

public sealed class ExportPreviewRequest
{
    public ExportPreviewRequest(
        IReadOnlyList<ViewPlan> selectedViews,
        ExportFeatureType featureTypes,
        UiLanguage uiLanguage)
    {
        SelectedViews = selectedViews ?? throw new ArgumentNullException(nameof(selectedViews));
        FeatureTypes = featureTypes;
        UiLanguage = uiLanguage;
    }

    public IReadOnlyList<ViewPlan> SelectedViews { get; }

    public ExportFeatureType FeatureTypes { get; }

    public UiLanguage UiLanguage { get; }
}
