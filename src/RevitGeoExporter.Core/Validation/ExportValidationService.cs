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
            issues.Add(CreateIssue(
                request,
                ValidationSeverity.Error,
                ValidationCode.InvalidTargetEpsg,
                "A valid EPSG code is required before export."));
        }

        foreach (ValidationViewSnapshot view in request.Views)
        {
            IReadOnlyList<ExportFeatureValidationSnapshot> features = view.Features;
            if (features.Count == 0)
            {
                issues.Add(CreateIssue(
                    request,
                    view,
                    ValidationSeverity.Warning,
                    ValidationCode.EmptyViewOutput,
                    $"View '{view.ViewName}' did not produce any exportable features."));
                continue;
            }

            AddFeatureIssues(
                issues,
                request,
                view,
                features,
                globalSeenIds,
                globalDuplicateIds,
                request.UnitSource,
                request.RoomCategoryParameterName);
            AddUnsupportedOpeningIssues(issues, request, view);
            AddVerticalAuditIssues(issues, request, view);

            if (CountSelectedOutputFeatures(request, features) == 0)
            {
                issues.Add(CreateIssue(
                    request,
                    view,
                    ValidationSeverity.Warning,
                    ValidationCode.EmptyViewOutput,
                    $"View '{view.ViewName}' did not produce any features for the selected export layers."));
            }
        }

        return new ExportValidationResult(issues);
    }

    private void AddFeatureIssues(
        ICollection<ValidationIssue> issues,
        ExportValidationRequest request,
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
                issues.Add(CreateIssue(
                    request,
                    view,
                    feature,
                    ValidationSeverity.Error,
                    ValidationCode.MissingStableId,
                    $"A {feature.FeatureType} feature in view '{view.ViewName}' is missing an export ID."));
            }
            else
            {
                string exportId = feature.ExportId!;
                if (!globalSeenIds.Add(exportId) && globalDuplicateIds.Add(exportId))
                {
                    issues.Add(CreateIssue(
                        request,
                        view,
                        feature,
                        ValidationSeverity.Error,
                        ValidationCode.DuplicateStableId,
                        $"Export ID '{exportId}' is duplicated across the selected export set."));
                }
            }

            if (!feature.HasGeometry)
            {
                issues.Add(CreateIssue(
                    request,
                    view,
                    feature,
                    ValidationSeverity.Warning,
                    ValidationCode.EmptyGeometry,
                    $"A {feature.FeatureType} feature in view '{view.ViewName}' has empty geometry."));
            }
            else if (!feature.GeometryValid)
            {
                issues.Add(CreateIssue(
                    request,
                    view,
                    feature,
                    ValidationSeverity.Warning,
                    ValidationCode.InvalidGeometry,
                    $"A {feature.FeatureType} feature in view '{view.ViewName}' has invalid geometry."));
            }

            if (feature.IsUnassigned)
            {
                string label = feature.AssignmentMappingKey ?? "<unknown mapping value>";
                string noun = unitSource == UnitSource.Rooms
                    ? $"Room-based unit value '{label}' for parameter '{roomCategoryParameterName}'"
                    : $"Floor-derived unit '{label}'";
                issues.Add(CreateIssue(
                    request,
                    view,
                    feature,
                    ValidationSeverity.Warning,
                    ValidationCode.UnassignedFloorCategory,
                    $"{noun} in view '{view.ViewName}' is still unassigned."));
            }

            if (string.Equals(feature.FeatureType, "unit", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(feature.Category) &&
                !ImdfUnitCategoryCatalog.IsOfficialCategory(feature.Category))
            {
                issues.Add(CreateIssue(
                    request,
                    view,
                    feature,
                    ValidationSeverity.Warning,
                    ValidationCode.NonStandardUnitCategory,
                    $"Unit category '{feature.Category}' in view '{view.ViewName}' is not an official IMDF unit category."));
            }

            if (string.Equals(feature.FeatureType, "opening", StringComparison.OrdinalIgnoreCase) &&
                !feature.IsSnappedToOutline)
            {
                issues.Add(CreateIssue(
                    request,
                    view,
                    feature,
                    ValidationSeverity.Warning,
                    ValidationCode.UnsnappedOpening,
                    $"Opening {feature.SourceElementId?.ToString() ?? "<unknown>"} in view '{view.ViewName}' could not be snapped to a unit outline."));
            }

            if (feature.IsLinkedSource && !feature.HasPersistedExportId)
            {
                string sourceDocument = feature.SourceDocumentKey ?? request.SourceDocumentKey ?? "linked document";
                issues.Add(CreateIssue(
                    request,
                    view,
                    feature,
                    ValidationSeverity.Warning,
                    ValidationCode.LinkedElementUsingFallbackId,
                    $"Linked {feature.FeatureType} element {feature.SourceElementId?.ToString() ?? "<unknown>"} from '{sourceDocument}' is using a deterministic fallback export ID."));
            }
        }
    }

    private static void AddUnsupportedOpeningIssues(
        ICollection<ValidationIssue> issues,
        ExportValidationRequest request,
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
                sourceElementId: opening.ElementId,
                owningViewId: view.ViewId,
                sourceDocumentKey: opening.SourceDocumentKey ?? request.SourceDocumentKey,
                actionKind: GetActionKind(ValidationCode.UnsupportedOpeningFamily),
                recommendedAction: GetRecommendedAction(ValidationCode.UnsupportedOpeningFamily),
                canNavigateInRevit: opening.CanNavigateInRevit));
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
                audit.Category,
                sourceElementId: null,
                owningViewId: view.ViewId,
                sourceDocumentKey: request.SourceDocumentKey,
                actionKind: ValidationActionKind.ReviewVerticalCirculation,
                recommendedAction: "Compare source vertical circulation elements against the prepared output in Revit before export continues.",
                canNavigateInRevit: true));
        }
    }

    private static ValidationIssue CreateIssue(
        ExportValidationRequest request,
        ValidationSeverity severity,
        ValidationCode code,
        string message)
    {
        return new ValidationIssue(
            severity,
            code,
            message,
            sourceDocumentKey: request.SourceDocumentKey,
            actionKind: GetActionKind(code),
            recommendedAction: GetRecommendedAction(code),
            canNavigateInRevit: false);
    }

    private static ValidationIssue CreateIssue(
        ExportValidationRequest request,
        ValidationViewSnapshot view,
        ValidationSeverity severity,
        ValidationCode code,
        string message)
    {
        return new ValidationIssue(
            severity,
            code,
            message,
            view.ViewName,
            view.LevelName,
            sourceElementId: null,
            owningViewId: view.ViewId,
            sourceDocumentKey: request.SourceDocumentKey,
            actionKind: GetActionKind(code),
            recommendedAction: GetRecommendedAction(code),
            canNavigateInRevit: true);
    }

    private static ValidationIssue CreateIssue(
        ExportValidationRequest request,
        ValidationViewSnapshot view,
        ExportFeatureValidationSnapshot? feature,
        ValidationSeverity severity,
        ValidationCode code,
        string message,
        long? sourceElementId = null,
        string? featureType = null)
    {
        return new ValidationIssue(
            severity,
            code,
            message,
            view.ViewName,
            view.LevelName,
            featureType ?? feature?.FeatureType,
            feature?.Category,
            sourceElementId ?? feature?.SourceElementId,
            view.ViewId,
            feature?.SourceDocumentKey ?? request.SourceDocumentKey,
            GetActionKind(code),
            GetRecommendedAction(code),
            canNavigateInRevit: !(feature?.IsLinkedSource ?? false) &&
                                ((sourceElementId ?? feature?.SourceElementId).HasValue || view.ViewId > 0));
    }

    private static ValidationActionKind GetActionKind(ValidationCode code)
    {
        switch (code)
        {
            case ValidationCode.InvalidTargetEpsg:
                return ValidationActionKind.ReviewExportSettings;
            case ValidationCode.MissingStableId:
            case ValidationCode.DuplicateStableId:
                return ValidationActionKind.RegenerateStableIds;
            case ValidationCode.UnassignedFloorCategory:
                return ValidationActionKind.ResolveMappings;
            case ValidationCode.LinkedElementUsingFallbackId:
                return ValidationActionKind.ReviewElementInRevit;
            case ValidationCode.UnsupportedOpeningFamily:
                return ValidationActionKind.ReviewOpeningFamilies;
            case ValidationCode.EmptyGeometry:
            case ValidationCode.InvalidGeometry:
            case ValidationCode.UnsnappedOpening:
                return ValidationActionKind.ReviewGeometry;
            case ValidationCode.MissingVerticalCirculation:
                return ValidationActionKind.ReviewVerticalCirculation;
            case ValidationCode.EmptyViewOutput:
            case ValidationCode.NonStandardUnitCategory:
            default:
                return ValidationActionKind.ReviewElementInRevit;
        }
    }

    private static string GetRecommendedAction(ValidationCode code)
    {
        switch (code)
        {
            case ValidationCode.InvalidTargetEpsg:
                return "Review the target EPSG and coordinate settings before export.";
            case ValidationCode.MissingStableId:
            case ValidationCode.DuplicateStableId:
                return "Regenerate exporter IDs for the affected elements before writing output.";
            case ValidationCode.UnassignedFloorCategory:
                return "Review the unassigned floor or room mapping and save an override if needed.";
            case ValidationCode.LinkedElementUsingFallbackId:
                return "Review the linked source element and assign a persisted exporter ID in the linked model if a stable cross-export ID is required.";
            case ValidationCode.UnsupportedOpeningFamily:
                return "Review accepted opening families or update project mappings for the unsupported family.";
            case ValidationCode.EmptyGeometry:
            case ValidationCode.InvalidGeometry:
            case ValidationCode.UnsnappedOpening:
                return "Inspect the affected element in Revit and adjust geometry or repair settings.";
            case ValidationCode.MissingVerticalCirculation:
                return "Compare source vertical circulation elements against the prepared output before continuing.";
            case ValidationCode.EmptyViewOutput:
                return "Review the selected view contents and export layer choices.";
            case ValidationCode.NonStandardUnitCategory:
                return "Review the resolved unit category and confirm whether a mapping override is needed.";
            default:
                return "Review the affected item in Revit before continuing.";
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
