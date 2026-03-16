using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Export;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Schema;

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
        UiLanguage uiLanguage,
        UnitSource unitSource,
        string roomCategoryParameterName,
        LinkExportOptions? linkExportOptions = null,
        SchemaProfile? activeSchemaProfile = null)
        : this(
            selectedViews,
            outputDirectory,
            targetEpsg,
            featureTypes,
            generateDiagnosticsReport,
            generatePackageOutput,
            includePackageLegend,
            geometryRepairOptions,
            selectedProfileName,
            uiLanguage,
            CoordinateExportMode.SharedCoordinates,
            unitSource,
            roomCategoryParameterName,
            linkExportOptions,
            activeSchemaProfile)
    {
    }

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
        UiLanguage uiLanguage,
        CoordinateExportMode coordinateMode,
        UnitSource unitSource,
        string roomCategoryParameterName,
        LinkExportOptions? linkExportOptions = null,
        SchemaProfile? activeSchemaProfile = null)
    {
        SelectedViews = selectedViews ?? throw new ArgumentNullException(nameof(selectedViews));
        OutputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
        TargetEpsg = targetEpsg;
        FeatureTypes = featureTypes;
        GenerateDiagnosticsReport = generateDiagnosticsReport;
        GeneratePackageOutput = generatePackageOutput;
        IncludePackageLegend = includePackageLegend;
        GeometryRepairOptions = geometryRepairOptions?.Clone() ?? throw new ArgumentNullException(nameof(geometryRepairOptions));
        SelectedProfileName = string.IsNullOrWhiteSpace(selectedProfileName) ? null : selectedProfileName.Trim();
        UiLanguage = uiLanguage;
        CoordinateMode = coordinateMode;
        UnitSource = unitSource;
        RoomCategoryParameterName = string.IsNullOrWhiteSpace(roomCategoryParameterName) ? "Name" : roomCategoryParameterName.Trim();
        LinkExportOptions = linkExportOptions?.Clone() ?? new LinkExportOptions();
        ActiveSchemaProfile = activeSchemaProfile?.Clone() ?? SchemaProfile.CreateCoreProfile();
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

    public CoordinateExportMode CoordinateMode { get; }

    public UnitSource UnitSource { get; }

    public string RoomCategoryParameterName { get; }

    public LinkExportOptions LinkExportOptions { get; }

    public SchemaProfile ActiveSchemaProfile { get; }
}
