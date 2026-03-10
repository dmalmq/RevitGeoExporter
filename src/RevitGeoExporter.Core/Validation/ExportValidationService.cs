using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Validation;

public sealed class ExportValidationService
{
    private readonly VerticalCirculationAudit _verticalCirculationAudit = new();
    private readonly ZoneCatalog _zoneCatalog = ZoneCatalog.CreateDefault();

    public ExportValidationResult Validate(ExportValidationRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        List<ValidationIssue> issues = new();
        HashSet<string> globalSeenIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> globalDuplicateIds = new(StringComparer.OrdinalIgnoreCase);

        if (request.TargetEpsg <= 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                ValidationCode.InvalidTargetEpsg,
                "A valid EPSG code is required before export."));
        }

        foreach (ValidationViewSnapshot view in request.Views)
        {
            IReadOnlyList<ExportFeatureValidationSnapshot> features = view.Features;
            if (features.Count == 0)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    ValidationCode.EmptyViewOutput,
                    $"View '{view.ViewName}' did not produce any exportable features.",
                    view.ViewName,
                    view.LevelName));
                continue;
            }

            AddFeatureIssues(issues, view, features, globalSeenIds, globalDuplicateIds, request.UnitSource, request.RoomCategoryParameterName);
            AddUnsupportedOpeningIssues(issues, view);
            AddVerticalAuditIssues(issues, request, view);

            if (CountSelectedOutputFeatures(request, features) == 0)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    ValidationCode.EmptyViewOutput,
                    $"View '{view.ViewName}' did not produce any features for the selected export layers.",
                    view.ViewName,
                    view.LevelName));
            }
        }

        return new ExportValidationResult(issues);
    }

    private void AddFeatureIssues(
        ICollection<ValidationIssue> issues,
        ValidationViewSnapshot view,
        IReadOnlyList<ExportFeatureValidationSnapshot> features,
        ISet<string> globalSeenIds,
        ISet<string> globalDuplicateIds,
        UnitSource unitSource,
        string roomCategoryParameterName)
    {
        foreach (ExportFeatureValidationSnapshot feature in features)
        {
            if (string.IsNullOrWhiteSpace(feature.ExportId))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    ValidationCode.MissingStableId,
                    $"A {feature.FeatureType} feature in view '{view.ViewName}' is missing an export ID.",
                    view.ViewName,
                    view.LevelName,
                    feature.FeatureType,
                    feature.Category,
                    feature.SourceElementId));
            }
            else
            {
                string exportId = feature.ExportId!;
                if (!globalSeenIds.Add(exportId) && globalDuplicateIds.Add(exportId))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error,
                        ValidationCode.DuplicateStableId,
                        $"Export ID '{exportId}' is duplicated across the selected export set.",
                        view.ViewName,
                        view.LevelName,
                        feature.FeatureType,
                        feature.Category,
                        feature.SourceElementId));
                }
            }

            if (!feature.HasGeometry)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    ValidationCode.EmptyGeometry,
                    $"A {feature.FeatureType} feature in view '{view.ViewName}' has empty geometry.",
                    view.ViewName,
                    view.LevelName,
                    feature.FeatureType,
                    feature.Category,
                    feature.SourceElementId));
            }
            else if (!feature.GeometryValid)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    ValidationCode.InvalidGeometry,
                    $"A {feature.FeatureType} feature in view '{view.ViewName}' has invalid geometry.",
                    view.ViewName,
                    view.LevelName,
                    feature.FeatureType,
                    feature.Category,
                    feature.SourceElementId));
            }

            if (feature.IsUnassigned)
            {
                string label = feature.AssignmentMappingKey ?? "<unknown mapping value>";
                string noun = unitSource == UnitSource.Rooms
                    ? $"Room-based unit value '{label}' for parameter '{roomCategoryParameterName}'"
                    : $"Floor-derived unit '{label}'";
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    ValidationCode.UnassignedFloorCategory,
                    $"{noun} in view '{view.ViewName}' is still unassigned.",
                    view.ViewName,
                    view.LevelName,
                    feature.FeatureType,
                    feature.Category,
                    feature.SourceElementId));
            }

            if (string.Equals(feature.FeatureType, "unit", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(feature.Category) &&
                !ImdfUnitCategoryCatalog.IsOfficialCategory(feature.Category))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    ValidationCode.NonStandardUnitCategory,
                    $"Unit category '{feature.Category}' in view '{view.ViewName}' is not an official IMDF unit category.",
                    view.ViewName,
                    view.LevelName,
                    feature.FeatureType,
                    feature.Category,
                    feature.SourceElementId));
            }

            if (string.Equals(feature.FeatureType, "opening", StringComparison.OrdinalIgnoreCase) &&
                !feature.IsSnappedToOutline)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    ValidationCode.UnsnappedOpening,
                    $"Opening {feature.SourceElementId?.ToString() ?? "<unknown>"} in view '{view.ViewName}' could not be snapped to a unit outline.",
                    view.ViewName,
                    view.LevelName,
                    feature.FeatureType,
                    feature.Category,
                    feature.SourceElementId));
            }
        }
    }

    private static void AddUnsupportedOpeningIssues(
        ICollection<ValidationIssue> issues,
        ValidationViewSnapshot view)
    {
        foreach (UnsupportedOpeningFamilySnapshot opening in view.UnsupportedOpenings)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                ValidationCode.UnsupportedOpeningFamily,
                $"Unsupported opening family '{opening.FamilyName}' was encountered in view '{view.ViewName}'.",
                view.ViewName,
                view.LevelName,
                "opening",
                sourceElementId: opening.ElementId));
        }
    }

    private void AddVerticalAuditIssues(
        ICollection<ValidationIssue> issues,
        ExportValidationRequest request,
        ValidationViewSnapshot view)
    {
        IReadOnlyList<VerticalCirculationAuditResult> audits = _verticalCirculationAudit.Audit(
            view,
            request.IncludeUnits,
            request.IncludeDetails,
            request.IncludeOpenings);
        foreach (VerticalCirculationAuditResult audit in audits.Where(result => result.HasMissingOutput))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                ValidationCode.MissingVerticalCirculation,
                $"View '{view.ViewName}' contains {audit.SourceCount} {audit.Category} source element(s), but only {audit.OutputCount} {audit.FeatureType} feature(s) were prepared.",
                view.ViewName,
                view.LevelName,
                audit.FeatureType,
                audit.Category));
        }
    }

    private static int CountSelectedOutputFeatures(
        ExportValidationRequest request,
        IReadOnlyList<ExportFeatureValidationSnapshot> features)
    {
        int count = 0;
        if (request.IncludeUnits)
        {
            count += features.Count(feature => string.Equals(feature.FeatureType, "unit", StringComparison.OrdinalIgnoreCase));
        }

        if (request.IncludeDetails)
        {
            count += features.Count(feature => string.Equals(feature.FeatureType, "detail", StringComparison.OrdinalIgnoreCase));
        }

        if (request.IncludeOpenings)
        {
            count += features.Count(feature => string.Equals(feature.FeatureType, "opening", StringComparison.OrdinalIgnoreCase));
        }

        if (request.IncludeLevels)
        {
            count += features.Count(feature => string.Equals(feature.FeatureType, "level", StringComparison.OrdinalIgnoreCase));
        }

        return count;
    }
}
