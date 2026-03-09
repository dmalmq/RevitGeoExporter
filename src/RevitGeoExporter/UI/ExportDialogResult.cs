using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoExporter.Export;

namespace RevitGeoExporter.UI;

public sealed class ExportDialogResult
{
    public ExportDialogResult(
        IReadOnlyList<ViewPlan> selectedViews,
        string outputDirectory,
        int targetEpsg,
        ExportFeatureType featureTypes,
        bool generateDiagnosticsReport,
        UiLanguage uiLanguage)
    {
        SelectedViews = selectedViews ?? throw new ArgumentNullException(nameof(selectedViews));
        OutputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
        TargetEpsg = targetEpsg;
        FeatureTypes = featureTypes;
        GenerateDiagnosticsReport = generateDiagnosticsReport;
        UiLanguage = uiLanguage;
    }

    public IReadOnlyList<ViewPlan> SelectedViews { get; }

    public string OutputDirectory { get; }

    public int TargetEpsg { get; }

    public ExportFeatureType FeatureTypes { get; }

    public bool GenerateDiagnosticsReport { get; }
    public UiLanguage UiLanguage { get; }
}
