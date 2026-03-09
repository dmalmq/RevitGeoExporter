using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Geometry;
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
        bool generatePackageOutput,
        bool includePackageLegend,
        GeometryRepairOptions geometryRepairOptions,
        string? selectedProfileName,
        UiLanguage uiLanguage)
    {
        SelectedViews = selectedViews ?? throw new ArgumentNullException(nameof(selectedViews));
        OutputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
        TargetEpsg = targetEpsg;
        FeatureTypes = featureTypes;
        GenerateDiagnosticsReport = generateDiagnosticsReport;
        GeneratePackageOutput = generatePackageOutput;
        IncludePackageLegend = includePackageLegend;
        GeometryRepairOptions = geometryRepairOptions?.Clone() ?? throw new ArgumentNullException(nameof(geometryRepairOptions));
        SelectedProfileName = string.IsNullOrWhiteSpace(selectedProfileName) ? null : selectedProfileName!.Trim();
        UiLanguage = uiLanguage;
    }

    public IReadOnlyList<ViewPlan> SelectedViews { get; }

    public string OutputDirectory { get; }

    public int TargetEpsg { get; }

    public ExportFeatureType FeatureTypes { get; }

    public bool GenerateDiagnosticsReport { get; }

    public bool GeneratePackageOutput { get; }

    public bool IncludePackageLegend { get; }

    public GeometryRepairOptions GeometryRepairOptions { get; }

    public string? SelectedProfileName { get; }

    public UiLanguage UiLanguage { get; }
}
