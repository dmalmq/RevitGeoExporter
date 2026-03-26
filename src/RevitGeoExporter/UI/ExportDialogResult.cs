using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Export;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Schema;
using RevitGeoExporter.Core.Validation;

namespace RevitGeoExporter.UI;

public sealed class ExportDialogResult
{
    public ExportDialogResult(
        IReadOnlyList<ViewPlan> selectedViews,
        string outputDirectory,
        int targetEpsg,
        ExportFeatureType featureTypes,
        IncrementalExportMode incrementalExportMode,
        bool generateDiagnosticsReport,
        bool generatePackageOutput,
        bool includePackageLegend,
        PackagingMode packagingMode,
        bool validateAfterWrite,
        bool generateQgisArtifacts,
        PostExportActionOptions? postExportActions,
        GeometryRepairOptions geometryRepairOptions,
        string? selectedProfileName,
        UiLanguage uiLanguage,
        UnitSource unitSource,
        UnitGeometrySource unitGeometrySource,
        UnitAttributeSource unitAttributeSource,
        string roomCategoryParameterName,
        LinkExportOptions? linkExportOptions = null,
        SchemaProfile? activeSchemaProfile = null,
        ValidationPolicyProfile? activeValidationPolicyProfile = null)
        : this(
            selectedViews,
            outputDirectory,
            targetEpsg,
            featureTypes,
            incrementalExportMode,
            generateDiagnosticsReport,
            generatePackageOutput,
            includePackageLegend,
            packagingMode,
            validateAfterWrite,
            generateQgisArtifacts,
            postExportActions,
            geometryRepairOptions,
            selectedProfileName,
            uiLanguage,
            CoordinateExportMode.SharedCoordinates,
            unitSource,
            unitGeometrySource,
            unitAttributeSource,
            roomCategoryParameterName,
            linkExportOptions,
            activeSchemaProfile,
            activeValidationPolicyProfile)
    {
    }

    public ExportDialogResult(
        IReadOnlyList<ViewPlan> selectedViews,
        string outputDirectory,
        int targetEpsg,
        ExportFeatureType featureTypes,
        IncrementalExportMode incrementalExportMode,
        bool generateDiagnosticsReport,
        bool generatePackageOutput,
        bool includePackageLegend,
        PackagingMode packagingMode,
        bool validateAfterWrite,
        bool generateQgisArtifacts,
        PostExportActionOptions? postExportActions,
        GeometryRepairOptions geometryRepairOptions,
        string? selectedProfileName,
        UiLanguage uiLanguage,
        CoordinateExportMode coordinateMode,
        UnitSource unitSource,
        UnitGeometrySource unitGeometrySource,
        UnitAttributeSource unitAttributeSource,
        string roomCategoryParameterName,
        LinkExportOptions? linkExportOptions = null,
        SchemaProfile? activeSchemaProfile = null,
        ValidationPolicyProfile? activeValidationPolicyProfile = null)
    {
        string? normalizedSelectedProfileName = selectedProfileName?.Trim();
        string normalizedRoomCategoryParameterName = roomCategoryParameterName?.Trim() ?? string.Empty;
        GeometryRepairOptions normalizedGeometryRepairOptions = geometryRepairOptions ?? throw new ArgumentNullException(nameof(geometryRepairOptions));

        if (string.IsNullOrEmpty(normalizedSelectedProfileName))
        {
            normalizedSelectedProfileName = null;
        }

        if (normalizedRoomCategoryParameterName.Length == 0)
        {
            normalizedRoomCategoryParameterName = "Name";
        }

        SelectedViews = selectedViews ?? throw new ArgumentNullException(nameof(selectedViews));
        OutputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
        TargetEpsg = targetEpsg;
        FeatureTypes = featureTypes;
        IncrementalExportMode = incrementalExportMode;
        GenerateDiagnosticsReport = generateDiagnosticsReport;
        GeneratePackageOutput = generatePackageOutput;
        IncludePackageLegend = includePackageLegend;
        PackagingMode = packagingMode;
        ValidateAfterWrite = validateAfterWrite;
        GenerateQgisArtifacts = generateQgisArtifacts;
        PostExportActions = postExportActions?.Clone() ?? new PostExportActionOptions();
        GeometryRepairOptions = normalizedGeometryRepairOptions.Clone();
        SelectedProfileName = normalizedSelectedProfileName;
        UiLanguage = uiLanguage;
        CoordinateMode = coordinateMode;
        UnitGeometrySource = UnitExportSettingsResolver.ResolveGeometrySource(unitSource, unitGeometrySource);
        UnitAttributeSource = UnitExportSettingsResolver.ResolveAttributeSource(unitSource, UnitGeometrySource, unitAttributeSource);
        UnitSource = UnitExportSettingsResolver.ToLegacy(UnitGeometrySource, UnitAttributeSource);
        RoomCategoryParameterName = normalizedRoomCategoryParameterName;
        LinkExportOptions = linkExportOptions?.Clone() ?? new LinkExportOptions();
        ActiveSchemaProfile = activeSchemaProfile?.Clone() ?? SchemaProfile.CreateCoreProfile();
        ActiveValidationPolicyProfile = activeValidationPolicyProfile?.Clone() ?? ValidationPolicyProfile.CreateRecommendedProfile();
    }

    public IReadOnlyList<ViewPlan> SelectedViews { get; }

    public string OutputDirectory { get; }

    public int TargetEpsg { get; }

    public ExportFeatureType FeatureTypes { get; }

    public IncrementalExportMode IncrementalExportMode { get; }

    public bool GenerateDiagnosticsReport { get; }

    public bool GeneratePackageOutput { get; }

    public bool IncludePackageLegend { get; }

    public PackagingMode PackagingMode { get; }

    public bool ValidateAfterWrite { get; }

    public bool GenerateQgisArtifacts { get; }

    public PostExportActionOptions PostExportActions { get; }

    public GeometryRepairOptions GeometryRepairOptions { get; }

    public string? SelectedProfileName { get; }

    public UiLanguage UiLanguage { get; }

    public CoordinateExportMode CoordinateMode { get; }

    public UnitSource UnitSource { get; }

    public UnitGeometrySource UnitGeometrySource { get; }

    public UnitAttributeSource UnitAttributeSource { get; }

    public string RoomCategoryParameterName { get; }

    public LinkExportOptions LinkExportOptions { get; }

    public SchemaProfile ActiveSchemaProfile { get; }

    public ValidationPolicyProfile ActiveValidationPolicyProfile { get; }

    public ExportFormat OutputFormat { get; set; } = ExportFormat.GeoPackage;
}
