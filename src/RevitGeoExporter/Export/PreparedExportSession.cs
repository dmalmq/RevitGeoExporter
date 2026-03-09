using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Geometry;

namespace RevitGeoExporter.Export;

public sealed class PreparedExportSession
{
    public PreparedExportSession(
        string outputDirectory,
        int targetEpsg,
        ExportFeatureType featureTypes,
        IReadOnlyList<ViewPlan> selectedViews,
        IReadOnlyList<ViewExportContext> contexts,
        FloorExportPreparationResult prepared,
        IReadOnlyDictionary<string, string> floorCategoryOverrides,
        GeometryRepairOptions geometryRepairOptions,
        ExportPackageOptions packageOptions,
        string? profileName,
        string baselineKey,
        string sourceModelName)
    {
        OutputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? throw new ArgumentException("An output directory is required.", nameof(outputDirectory))
            : outputDirectory.Trim();
        TargetEpsg = targetEpsg;
        FeatureTypes = featureTypes;
        SelectedViews = selectedViews ?? throw new ArgumentNullException(nameof(selectedViews));
        Contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        Prepared = prepared ?? throw new ArgumentNullException(nameof(prepared));
        FloorCategoryOverrides = floorCategoryOverrides ?? throw new ArgumentNullException(nameof(floorCategoryOverrides));
        GeometryRepairOptions = geometryRepairOptions?.Clone() ?? throw new ArgumentNullException(nameof(geometryRepairOptions));
        PackageOptions = packageOptions ?? throw new ArgumentNullException(nameof(packageOptions));
        ProfileName = string.IsNullOrWhiteSpace(profileName) ? null : profileName!.Trim();
        BaselineKey = string.IsNullOrWhiteSpace(baselineKey) ? throw new ArgumentException("A baseline key is required.", nameof(baselineKey)) : baselineKey!.Trim();
        SourceModelName = string.IsNullOrWhiteSpace(sourceModelName) ? "Model" : sourceModelName.Trim();
    }

    public string OutputDirectory { get; }

    public int TargetEpsg { get; }

    public ExportFeatureType FeatureTypes { get; }

    public IReadOnlyList<ViewPlan> SelectedViews { get; }

    public IReadOnlyList<ViewExportContext> Contexts { get; }

    public FloorExportPreparationResult Prepared { get; }

    public IReadOnlyDictionary<string, string> FloorCategoryOverrides { get; }

    public GeometryRepairOptions GeometryRepairOptions { get; }

    public ExportPackageOptions PackageOptions { get; }

    public string? ProfileName { get; }

    public string BaselineKey { get; }

    public string SourceModelName { get; }
}
