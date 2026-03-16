using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Export;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Schema;

namespace RevitGeoExporter.UI;

public sealed class ExportProfile
{
    public string Name { get; set; } = string.Empty;

    public ExportProfileScope Scope { get; set; } = ExportProfileScope.Project;

    public string OutputDirectory { get; set; } = string.Empty;

    public int TargetEpsg { get; set; } = ProjectInfo.DefaultTargetEpsg;

    public ExportFeatureType FeatureTypes { get; set; } = ExportFeatureType.All;

    public bool GenerateDiagnosticsReport { get; set; } = true;

    public bool GeneratePackageOutput { get; set; }

    public bool IncludePackageLegend { get; set; } = true;

    public GeometryRepairOptions GeometryRepairOptions { get; set; } = new();

    public UiLanguage UiLanguage { get; set; } = UiLanguage.English;

    public CoordinateExportMode CoordinateMode { get; set; } = CoordinateExportMode.SharedCoordinates;

    public UnitSource UnitSource { get; set; } = UnitSource.Floors;

    public string RoomCategoryParameterName { get; set; } = "Name";

    public LinkExportOptions LinkExportOptions { get; set; } = new();

    public List<SchemaProfile> SchemaProfiles { get; set; } = new() { SchemaProfile.CreateCoreProfile() };

    public string ActiveSchemaProfileName { get; set; } = SchemaProfile.CoreProfileName;

    public ExportDialogSettings ToSettings()
    {
        List<SchemaProfile> schemaProfiles = CloneSchemaProfiles(SchemaProfiles);
        return new ExportDialogSettings
        {
            OutputDirectory = OutputDirectory,
            TargetEpsg = TargetEpsg,
            FeatureTypes = FeatureTypes,
            GenerateDiagnosticsReport = GenerateDiagnosticsReport,
            GeneratePackageOutput = GeneratePackageOutput,
            IncludePackageLegend = IncludePackageLegend,
            GeometryRepairOptions = GeometryRepairOptions?.Clone() ?? new GeometryRepairOptions(),
            UiLanguage = UiLanguage,
            CoordinateMode = CoordinateMode,
            UnitSource = UnitSource,
            RoomCategoryParameterName = RoomCategoryParameterName,
            LinkExportOptions = LinkExportOptions?.Clone() ?? new LinkExportOptions(),
            SchemaProfiles = schemaProfiles,
            ActiveSchemaProfileName = SchemaProfile.ResolveActiveName(schemaProfiles, ActiveSchemaProfileName),
        };
    }

    public static ExportProfile FromSettings(string name, ExportProfileScope scope, ExportDialogSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        List<SchemaProfile> schemaProfiles = CloneSchemaProfiles(settings.SchemaProfiles);

        return new ExportProfile
        {
            Name = name?.Trim() ?? string.Empty,
            Scope = scope,
            OutputDirectory = settings.OutputDirectory,
            TargetEpsg = settings.TargetEpsg,
            FeatureTypes = settings.FeatureTypes,
            GenerateDiagnosticsReport = settings.GenerateDiagnosticsReport,
            GeneratePackageOutput = settings.GeneratePackageOutput,
            IncludePackageLegend = settings.IncludePackageLegend,
            GeometryRepairOptions = settings.GeometryRepairOptions?.Clone() ?? new GeometryRepairOptions(),
            UiLanguage = settings.UiLanguage,
            CoordinateMode = settings.CoordinateMode,
            UnitSource = settings.UnitSource,
            RoomCategoryParameterName = settings.RoomCategoryParameterName?.Trim() ?? "Name",
            LinkExportOptions = settings.LinkExportOptions?.Clone() ?? new LinkExportOptions(),
            SchemaProfiles = schemaProfiles,
            ActiveSchemaProfileName = SchemaProfile.ResolveActiveName(schemaProfiles, settings.ActiveSchemaProfileName),
        };
    }

    private static List<SchemaProfile> CloneSchemaProfiles(IEnumerable<SchemaProfile>? schemaProfiles)
    {
        return SchemaProfile.NormalizeProfiles(schemaProfiles)
            .Select(profile => profile.Clone())
            .ToList();
    }
}
