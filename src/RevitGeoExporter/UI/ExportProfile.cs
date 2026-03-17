using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Export;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Schema;
using RevitGeoExporter.Core.Validation;

namespace RevitGeoExporter.UI;

public sealed class ExportProfile
{
    public string Name { get; set; } = string.Empty;

    public ExportProfileScope Scope { get; set; } = ExportProfileScope.Project;

    public string OutputDirectory { get; set; } = string.Empty;

    public int TargetEpsg { get; set; } = ProjectInfo.DefaultTargetEpsg;

    public ExportFeatureType FeatureTypes { get; set; } = ExportFeatureType.All;

    public List<long> SelectedViewIds { get; set; } = new();

    public IncrementalExportMode IncrementalExportMode { get; set; } = IncrementalExportMode.AllSelectedViews;

    public bool GenerateDiagnosticsReport { get; set; } = true;

    public bool GeneratePackageOutput { get; set; }

    public bool IncludePackageLegend { get; set; } = true;

    public PackagingMode PackagingMode { get; set; } = PackagingMode.PerViewPerFeatureFiles;

    public bool ValidateAfterWrite { get; set; } = true;

    public bool GenerateQgisArtifacts { get; set; }

    public PostExportActionOptions PostExportActions { get; set; } = new();

    public GeometryRepairOptions GeometryRepairOptions { get; set; } = new();

    public UiLanguage UiLanguage { get; set; } = UiLanguage.English;

    public CoordinateExportMode CoordinateMode { get; set; } = CoordinateExportMode.SharedCoordinates;

    public UnitSource UnitSource { get; set; } = UnitSource.Floors;

    public UnitGeometrySource UnitGeometrySource { get; set; } = UnitGeometrySource.Unset;

    public UnitAttributeSource UnitAttributeSource { get; set; } = UnitAttributeSource.Unset;

    public string RoomCategoryParameterName { get; set; } = "Name";

    public LinkExportOptions LinkExportOptions { get; set; } = new();

    public List<SchemaProfile> SchemaProfiles { get; set; } = new() { SchemaProfile.CreateCoreProfile() };

    public string ActiveSchemaProfileName { get; set; } = SchemaProfile.CoreProfileName;

    public List<ValidationPolicyProfile> ValidationPolicyProfiles { get; set; } = ValidationPolicyProfile.NormalizeProfiles(null);

    public string ActiveValidationPolicyProfileName { get; set; } = ValidationPolicyProfile.RecommendedProfileName;

    public ExportDialogSettings ToSettings()
    {
        List<SchemaProfile> schemaProfiles = CloneSchemaProfiles(SchemaProfiles);
        List<ValidationPolicyProfile> validationPolicyProfiles = CloneValidationPolicyProfiles(ValidationPolicyProfiles);
        UnitGeometrySource geometrySource = UnitExportSettingsResolver.ResolveGeometrySource(UnitSource, UnitGeometrySource);
        UnitAttributeSource attributeSource = UnitExportSettingsResolver.ResolveAttributeSource(UnitSource, geometrySource, UnitAttributeSource);
        return new ExportDialogSettings
        {
            OutputDirectory = OutputDirectory,
            TargetEpsg = TargetEpsg,
            FeatureTypes = FeatureTypes,
            SelectedViewIds = SelectedViewIds?.Distinct().OrderBy(id => id).ToList() ?? new List<long>(),
            IncrementalExportMode = IncrementalExportMode,
            GenerateDiagnosticsReport = GenerateDiagnosticsReport,
            GeneratePackageOutput = GeneratePackageOutput,
            IncludePackageLegend = IncludePackageLegend,
            PackagingMode = PackagingMode,
            ValidateAfterWrite = ValidateAfterWrite,
            GenerateQgisArtifacts = GenerateQgisArtifacts,
            PostExportActions = PostExportActions?.Clone() ?? new PostExportActionOptions(),
            GeometryRepairOptions = GeometryRepairOptions?.Clone() ?? new GeometryRepairOptions(),
            UiLanguage = UiLanguage,
            CoordinateMode = CoordinateMode,
            UnitSource = UnitExportSettingsResolver.ToLegacy(geometrySource, attributeSource),
            UnitGeometrySource = geometrySource,
            UnitAttributeSource = attributeSource,
            RoomCategoryParameterName = RoomCategoryParameterName,
            LinkExportOptions = LinkExportOptions?.Clone() ?? new LinkExportOptions(),
            SchemaProfiles = schemaProfiles,
            ActiveSchemaProfileName = SchemaProfile.ResolveActiveName(schemaProfiles, ActiveSchemaProfileName),
            ValidationPolicyProfiles = validationPolicyProfiles,
            ActiveValidationPolicyProfileName = ValidationPolicyProfile.ResolveActiveName(validationPolicyProfiles, ActiveValidationPolicyProfileName),
        };
    }

    public static ExportProfile FromSettings(string name, ExportProfileScope scope, ExportDialogSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        List<SchemaProfile> schemaProfiles = CloneSchemaProfiles(settings.SchemaProfiles);
        List<ValidationPolicyProfile> validationPolicyProfiles = CloneValidationPolicyProfiles(settings.ValidationPolicyProfiles);
        UnitGeometrySource geometrySource = UnitExportSettingsResolver.ResolveGeometrySource(settings.UnitSource, settings.UnitGeometrySource);
        UnitAttributeSource attributeSource = UnitExportSettingsResolver.ResolveAttributeSource(settings.UnitSource, geometrySource, settings.UnitAttributeSource);

        return new ExportProfile
        {
            Name = name?.Trim() ?? string.Empty,
            Scope = scope,
            OutputDirectory = settings.OutputDirectory,
            TargetEpsg = settings.TargetEpsg,
            FeatureTypes = settings.FeatureTypes,
            SelectedViewIds = (settings.SelectedViewIds ?? new List<long>()).Distinct().OrderBy(id => id).ToList(),
            IncrementalExportMode = settings.IncrementalExportMode,
            GenerateDiagnosticsReport = settings.GenerateDiagnosticsReport,
            GeneratePackageOutput = settings.GeneratePackageOutput,
            IncludePackageLegend = settings.IncludePackageLegend,
            PackagingMode = settings.PackagingMode,
            ValidateAfterWrite = settings.ValidateAfterWrite,
            GenerateQgisArtifacts = settings.GenerateQgisArtifacts,
            PostExportActions = settings.PostExportActions?.Clone() ?? new PostExportActionOptions(),
            GeometryRepairOptions = settings.GeometryRepairOptions?.Clone() ?? new GeometryRepairOptions(),
            UiLanguage = settings.UiLanguage,
            CoordinateMode = settings.CoordinateMode,
            UnitSource = UnitExportSettingsResolver.ToLegacy(geometrySource, attributeSource),
            UnitGeometrySource = geometrySource,
            UnitAttributeSource = attributeSource,
            RoomCategoryParameterName = settings.RoomCategoryParameterName?.Trim() ?? "Name",
            LinkExportOptions = settings.LinkExportOptions?.Clone() ?? new LinkExportOptions(),
            SchemaProfiles = schemaProfiles,
            ActiveSchemaProfileName = SchemaProfile.ResolveActiveName(schemaProfiles, settings.ActiveSchemaProfileName),
            ValidationPolicyProfiles = validationPolicyProfiles,
            ActiveValidationPolicyProfileName = ValidationPolicyProfile.ResolveActiveName(validationPolicyProfiles, settings.ActiveValidationPolicyProfileName),
        };
    }

    private static List<SchemaProfile> CloneSchemaProfiles(IEnumerable<SchemaProfile>? schemaProfiles)
    {
        return SchemaProfile.NormalizeProfiles(schemaProfiles)
            .Select(profile => profile.Clone())
            .ToList();
    }

    private static List<ValidationPolicyProfile> CloneValidationPolicyProfiles(IEnumerable<ValidationPolicyProfile>? validationPolicyProfiles)
    {
        return ValidationPolicyProfile.NormalizeProfiles(validationPolicyProfiles)
            .Select(profile => profile.Clone())
            .ToList();
    }
}
