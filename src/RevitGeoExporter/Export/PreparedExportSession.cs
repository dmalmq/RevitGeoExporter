using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Schema;
using RevitGeoExporter.Core.Validation;

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
        IncrementalExportMode incrementalExportMode,
        IReadOnlyDictionary<string, string> floorCategoryOverrides,
        IReadOnlyDictionary<string, string> roomCategoryOverrides,
        IReadOnlyDictionary<string, string> familyCategoryOverrides,
        IReadOnlyList<string> acceptedOpeningFamilies,
        GeometryRepairOptions geometryRepairOptions,
        ExportPackageOptions packageOptions,
        string? profileName,
        string baselineKey,
        string sourceDocumentKey,
        string sourceModelName,
        CoordinateExportMode coordinateMode,
        int? sourceEpsg,
        string? sourceCoordinateSystemId,
        string? sourceCoordinateSystemDefinition,
        UnitSource unitSource,
        UnitGeometrySource unitGeometrySource,
        UnitAttributeSource unitAttributeSource,
        string roomCategoryParameterName,
        LinkExportOptions? linkExportOptions,
        SchemaProfile? activeSchemaProfile,
        ValidationPolicyProfile? activeValidationPolicyProfile,
        IReadOnlyList<LinkedModelSummary>? includedLinks)
    {
        OutputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? throw new ArgumentException("An output directory is required.", nameof(outputDirectory))
            : outputDirectory.Trim();
        TargetEpsg = targetEpsg;
        FeatureTypes = featureTypes;
        SelectedViews = selectedViews ?? throw new ArgumentNullException(nameof(selectedViews));
        Contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        Prepared = prepared ?? throw new ArgumentNullException(nameof(prepared));
        IncrementalExportMode = incrementalExportMode;
        FloorCategoryOverrides = floorCategoryOverrides ?? throw new ArgumentNullException(nameof(floorCategoryOverrides));
        RoomCategoryOverrides = roomCategoryOverrides ?? throw new ArgumentNullException(nameof(roomCategoryOverrides));
        FamilyCategoryOverrides = familyCategoryOverrides ?? throw new ArgumentNullException(nameof(familyCategoryOverrides));
        AcceptedOpeningFamilies = acceptedOpeningFamilies ?? throw new ArgumentNullException(nameof(acceptedOpeningFamilies));
        GeometryRepairOptions = geometryRepairOptions?.Clone() ?? throw new ArgumentNullException(nameof(geometryRepairOptions));
        PackageOptions = packageOptions ?? throw new ArgumentNullException(nameof(packageOptions));
        ProfileName = string.IsNullOrWhiteSpace(profileName) ? null : profileName.Trim();
        BaselineKey = string.IsNullOrWhiteSpace(baselineKey) ? throw new ArgumentException("A baseline key is required.", nameof(baselineKey)) : baselineKey.Trim();
        SourceDocumentKey = string.IsNullOrWhiteSpace(sourceDocumentKey)
            ? throw new ArgumentException("A source document key is required.", nameof(sourceDocumentKey))
            : sourceDocumentKey.Trim();
        SourceModelName = string.IsNullOrWhiteSpace(sourceModelName) ? "Model" : sourceModelName.Trim();
        CoordinateMode = coordinateMode;
        SourceEpsg = sourceEpsg;
        SourceCoordinateSystemId = string.IsNullOrWhiteSpace(sourceCoordinateSystemId) ? null : sourceCoordinateSystemId.Trim();
        SourceCoordinateSystemDefinition = string.IsNullOrWhiteSpace(sourceCoordinateSystemDefinition) ? null : sourceCoordinateSystemDefinition.Trim();
        OutputEpsg = coordinateMode == CoordinateExportMode.SharedCoordinates
            ? sourceEpsg ?? targetEpsg
            : targetEpsg;
        UnitSource = unitSource;
        UnitGeometrySource = UnitExportSettingsResolver.ResolveGeometrySource(unitSource, unitGeometrySource);
        UnitAttributeSource = UnitExportSettingsResolver.ResolveAttributeSource(unitSource, UnitGeometrySource, unitAttributeSource);
        RoomCategoryParameterName = string.IsNullOrWhiteSpace(roomCategoryParameterName) ? "Name" : roomCategoryParameterName.Trim();
        LinkExportOptions = linkExportOptions?.Clone() ?? new LinkExportOptions();
        ActiveSchemaProfile = activeSchemaProfile?.Clone() ?? SchemaProfile.CreateCoreProfile();
        ActiveValidationPolicyProfile = activeValidationPolicyProfile?.Clone() ?? ValidationPolicyProfile.CreateRecommendedProfile();
        IncludedLinks = includedLinks ?? Array.Empty<LinkedModelSummary>();
    }

    public string OutputDirectory { get; }

    public int TargetEpsg { get; }

    public ExportFeatureType FeatureTypes { get; }

    public IReadOnlyList<ViewPlan> SelectedViews { get; }

    public IReadOnlyList<ViewExportContext> Contexts { get; }

    public FloorExportPreparationResult Prepared { get; }

    public IncrementalExportMode IncrementalExportMode { get; }

    public IReadOnlyDictionary<string, string> FloorCategoryOverrides { get; }

    public IReadOnlyDictionary<string, string> RoomCategoryOverrides { get; }

    public IReadOnlyDictionary<string, string> FamilyCategoryOverrides { get; }

    public IReadOnlyList<string> AcceptedOpeningFamilies { get; }

    public GeometryRepairOptions GeometryRepairOptions { get; }

    public ExportPackageOptions PackageOptions { get; }

    public string? ProfileName { get; }

    public string BaselineKey { get; }

    public string SourceDocumentKey { get; }

    public string SourceModelName { get; }

    public CoordinateExportMode CoordinateMode { get; }

    public int? SourceEpsg { get; }

    public string? SourceCoordinateSystemId { get; }

    public string? SourceCoordinateSystemDefinition { get; }

    public int OutputEpsg { get; }

    public UnitSource UnitSource { get; }

    public UnitGeometrySource UnitGeometrySource { get; }

    public UnitAttributeSource UnitAttributeSource { get; }

    public string RoomCategoryParameterName { get; }

    public LinkExportOptions LinkExportOptions { get; }

    public SchemaProfile ActiveSchemaProfile { get; }

    public ValidationPolicyProfile ActiveValidationPolicyProfile { get; }

    public IReadOnlyList<LinkedModelSummary> IncludedLinks { get; }

    public ExportFormat OutputFormat { get; set; } = ExportFormat.GeoPackage;
}
