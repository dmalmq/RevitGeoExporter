using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;

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
        IReadOnlyDictionary<string, string> roomCategoryOverrides,
        GeometryRepairOptions geometryRepairOptions,
        ExportPackageOptions packageOptions,
        string? profileName,
        string baselineKey,
        string sourceModelName,
        CoordinateExportMode coordinateMode,
        int? sourceEpsg,
        string? sourceCoordinateSystemId,
        string? sourceCoordinateSystemDefinition,
        UnitSource unitSource,
        string roomCategoryParameterName)
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
        RoomCategoryOverrides = roomCategoryOverrides ?? throw new ArgumentNullException(nameof(roomCategoryOverrides));
        GeometryRepairOptions = geometryRepairOptions?.Clone() ?? throw new ArgumentNullException(nameof(geometryRepairOptions));
        PackageOptions = packageOptions ?? throw new ArgumentNullException(nameof(packageOptions));
        ProfileName = string.IsNullOrWhiteSpace(profileName) ? null : profileName.Trim();
        BaselineKey = string.IsNullOrWhiteSpace(baselineKey) ? throw new ArgumentException("A baseline key is required.", nameof(baselineKey)) : baselineKey.Trim();
        SourceModelName = string.IsNullOrWhiteSpace(sourceModelName) ? "Model" : sourceModelName.Trim();
        CoordinateMode = coordinateMode;
        SourceEpsg = sourceEpsg;
        SourceCoordinateSystemId = string.IsNullOrWhiteSpace(sourceCoordinateSystemId) ? null : sourceCoordinateSystemId.Trim();
        SourceCoordinateSystemDefinition = string.IsNullOrWhiteSpace(sourceCoordinateSystemDefinition) ? null : sourceCoordinateSystemDefinition.Trim();
        OutputEpsg = coordinateMode == CoordinateExportMode.SharedCoordinates
            ? sourceEpsg ?? targetEpsg
            : targetEpsg;
        UnitSource = unitSource;
        RoomCategoryParameterName = string.IsNullOrWhiteSpace(roomCategoryParameterName) ? "Name" : roomCategoryParameterName.Trim();
    }

    public string OutputDirectory { get; }

    public int TargetEpsg { get; }

    public ExportFeatureType FeatureTypes { get; }

    public IReadOnlyList<ViewPlan> SelectedViews { get; }

    public IReadOnlyList<ViewExportContext> Contexts { get; }

    public FloorExportPreparationResult Prepared { get; }

    public IReadOnlyDictionary<string, string> FloorCategoryOverrides { get; }

    public IReadOnlyDictionary<string, string> RoomCategoryOverrides { get; }

    public GeometryRepairOptions GeometryRepairOptions { get; }

    public ExportPackageOptions PackageOptions { get; }

    public string? ProfileName { get; }

    public string BaselineKey { get; }

    public string SourceModelName { get; }

    public CoordinateExportMode CoordinateMode { get; }

    public int? SourceEpsg { get; }

    public string? SourceCoordinateSystemId { get; }

    public string? SourceCoordinateSystemDefinition { get; }

    public int OutputEpsg { get; }

    public UnitSource UnitSource { get; }

    public string RoomCategoryParameterName { get; }
}