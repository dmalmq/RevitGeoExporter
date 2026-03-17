using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Diagnostics;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Validation;

namespace RevitGeoExporter.Export;

public sealed class ExportDiagnosticsReportBuilder
{
    public ExportDiagnosticsReport Build(
        PreparedExportSession session,
        ExportValidationResult validationResult,
        FloorGeoPackageExportResult exportResult,
        DateTimeOffset exportedAtUtc,
        TimeSpan duration)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        if (validationResult is null)
        {
            throw new ArgumentNullException(nameof(validationResult));
        }

        if (exportResult is null)
        {
            throw new ArgumentNullException(nameof(exportResult));
        }

        Dictionary<long, PreparedViewExportData> preparedByViewId = session.Prepared.Views
            .ToDictionary(view => view.View.Id.Value);
        List<ExportDiagnosticsViewReport> views = new();
        foreach (ViewExportContext context in session.Contexts)
        {
            preparedByViewId.TryGetValue(context.View.Id.Value, out PreparedViewExportData? prepared);
            views.Add(BuildViewReport(context, prepared));
        }

        return new ExportDiagnosticsReport
        {
            SourceModelName = session.SourceModelName,
            SourceDocumentKey = session.SourceDocumentKey,
            TargetEpsg = session.OutputEpsg,
            SourceEpsg = session.SourceEpsg,
            SourceCoordinateSystemId = session.SourceCoordinateSystemId,
            SourceCoordinateSystemDefinition = session.SourceCoordinateSystemDefinition,
            ProfileName = session.ProfileName,
            SchemaProfileName = session.ActiveSchemaProfile.Name,
            ValidationPolicyProfileName = session.ActiveValidationPolicyProfile.Name,
            OperatorName = Environment.UserName ?? string.Empty,
            CoordinateMode = session.CoordinateMode.ToString(),
            PackagingMode = session.PackageOptions.PackagingMode.ToString(),
            ExportedAtUtc = exportedAtUtc,
            DurationMilliseconds = (long)Math.Max(0d, duration.TotalMilliseconds),
            Views = views,
            ValidationIssues = validationResult.Issues.ToList(),
            ExportWarnings = exportResult.Warnings.ToList(),
            IncludedLinks = session.IncludedLinks
                .Select(link => ExportLinkedModelInfo.Create(
                    link.LinkInstanceId,
                    link.LinkInstanceName,
                    link.SourceDocumentKey,
                    link.SourceDocumentName))
                .ToList(),
            OutputFiles = exportResult.ArtifactResults
                .Select(result => new ExportDiagnosticsOutputFile
                {
                    ViewName = result.ContributingViewNames.FirstOrDefault() ?? string.Empty,
                    ViewId = result.ContributingViewIds.FirstOrDefault(),
                    FeatureType = result.LayerSummary,
                    Path = result.OutputFilePath,
                    FeatureCount = result.FeatureCount,
                    ArtifactKey = result.ArtifactKey,
                    RelativePath = Path.GetFileName(result.OutputFilePath),
                    PackagingMode = result.PackagingMode.ToString(),
                    Disposition = result.Disposition.ToString(),
                    ContributingViewIds = result.ContributingViewIds.ToList(),
                    ContributingViewNames = result.ContributingViewNames.ToList(),
                    ContributingLevelNames = result.ContributingLevelNames.ToList(),
                    LayerNames = result.LayerNames.ToList(),
                })
                .ToList(),
            PackageValidationResult = exportResult.PackageValidationResult,
        };
    }

    private static ExportDiagnosticsViewReport BuildViewReport(
        ViewExportContext context,
        PreparedViewExportData? prepared)
    {
        ExportDiagnosticsViewReport report = new()
        {
            ViewId = context.View.Id.Value,
            ViewName = context.View.Name,
            LevelName = context.Level.Name,
            UnsupportedOpeningFamilies = context.UnsupportedOpenings
                .Concat(context.LinkedSources.SelectMany(source => source.UnsupportedOpenings))
                .GroupBy(opening => OpeningFamilyClassifier.GetFamilyName(opening), StringComparer.OrdinalIgnoreCase)
                .Select(group => new ExportDiagnosticsFamilyOccurrence
                {
                    FamilyName = group.Key,
                    Count = group.Count(),
                })
                .OrderBy(group => group.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };

        if (prepared == null)
        {
            return report;
        }

        AddLayerCounts(report.Layers, "unit", prepared.UnitLayer?.Features);
        AddLayerCounts(report.Layers, "detail", prepared.DetailLayer?.Features);
        AddLayerCounts(report.Layers, "opening", prepared.OpeningLayer?.Features);
        AddLayerCounts(report.Layers, "level", prepared.LevelLayer?.Features);

        report.UnsnappedOpeningCount = prepared.OpeningLayer?.Features
            .OfType<ExportLineString>()
            .Count(feature => !ReadBool(feature.Attributes, "is_snapped_to_outline", defaultValue: true))
            ?? 0;
        report.DroppedPolygonCount = prepared.GeometryRepair.DroppedPolygons;
        report.DroppedOpeningCount = prepared.GeometryRepair.DroppedOpenings;
        report.SimplifiedPolygonCount = prepared.GeometryRepair.SimplifiedPolygons;

        report.UnassignedFloorTypes = prepared.UnitLayer?.Features
            .OfType<ExportPolygon>()
            .Where(feature => ReadBool(feature.Attributes, "is_unassigned"))
            .GroupBy(feature => ReadString(feature.Attributes, "source_floor_type_name") ?? "<unknown floor type>", StringComparer.Ordinal)
            .Select(group => new ExportDiagnosticsUnassignedFloorGroup
            {
                FloorTypeName = group.Key,
                Count = group.Count(),
            })
            .OrderBy(group => group.FloorTypeName, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<ExportDiagnosticsUnassignedFloorGroup>();

        report.AppliedFloorOverrides = prepared.UnitLayer?.Features
            .OfType<ExportPolygon>()
            .Where(feature => string.Equals(
                ReadString(feature.Attributes, "category_resolution_source"),
                FloorCategoryResolutionSource.Override.ToString(),
                StringComparison.OrdinalIgnoreCase))
            .GroupBy(feature =>
                $"{ReadString(feature.Attributes, "source_floor_type_name") ?? "<unknown floor type>"}|{ReadString(feature.Attributes, "category") ?? "unspecified"}",
                StringComparer.Ordinal)
            .Select(group => new ExportDiagnosticsFloorOverride
            {
                FloorTypeName = group.Key.Split(new[] { '|' }, 2)[0],
                Category = group.Key.Split(new[] { '|' }, 2).Length > 1
                    ? group.Key.Split(new[] { '|' }, 2)[1]
                    : "unspecified",
                Count = group.Count(),
            })
            .OrderBy(group => group.FloorTypeName, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<ExportDiagnosticsFloorOverride>();

        return report;
    }

    private static void AddLayerCounts(
        ICollection<ExportDiagnosticsLayerCount> target,
        string featureType,
        IEnumerable<IExportFeature>? features)
    {
        if (features == null)
        {
            return;
        }

        foreach (IGrouping<string?, IExportFeature> group in features.GroupBy(feature => ReadString(feature.Attributes, "category")))
        {
            target.Add(new ExportDiagnosticsLayerCount
            {
                FeatureType = featureType,
                Category = group.Key,
                Count = group.Count(),
            });
        }
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out object? value) || value == null)
        {
            return null;
        }

        string trimmed = value.ToString()?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, object?> attributes, string key, bool defaultValue = false)
    {
        if (!attributes.TryGetValue(key, out object? value) || value == null)
        {
            return defaultValue;
        }

        return value switch
        {
            bool boolValue => boolValue,
            string stringValue when bool.TryParse(stringValue, out bool parsed) => parsed,
            _ => defaultValue,
        };
    }
}
