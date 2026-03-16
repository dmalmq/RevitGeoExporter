using System;
using System.Linq;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Validation;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Validation;

public sealed class ExportValidationServiceTests
{
    [Fact]
    public void Validate_ReturnsWarningForUnassignedFloorUnits()
    {
        ExportValidationService service = new();
        ExportValidationResult result = service.Validate(
            CreateRequest(
                6677,
                includeUnits: true,
                includeDetails: false,
                includeOpenings: false,
                includeLevels: false,
                new[]
                {
                    new ValidationViewSnapshot(
                        1,
                        "View A",
                        "L1",
                        new[]
                        {
                            new ExportFeatureValidationSnapshot(
                                "unit",
                                "unit-1",
                                "unspecified",
                                101,
                                hasGeometry: true,
                                geometryValid: true,
                                isUnassigned: true,
                                assignmentMappingKey: "Wrong Floor Name"),
                        }),
                }));

        ValidationIssue issue = Assert.Single(result.Issues);
        Assert.Equal(ValidationCode.UnassignedFloorCategory, issue.Code);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Equal(ValidationActionKind.ResolveMappings, issue.ActionKind);
        Assert.Equal(101, issue.SourceElementId);
        Assert.Equal(1, issue.OwningViewId);
        Assert.Equal("TestModel", issue.SourceDocumentKey);
        Assert.True(issue.CanNavigateInRevit);
        Assert.False(string.IsNullOrWhiteSpace(issue.RecommendedAction));
    }

    [Fact]
    public void Validate_ReturnsErrorForMissingOrDuplicateStableIds()
    {
        ExportValidationService service = new();
        ExportValidationResult result = service.Validate(
            CreateRequest(
                6677,
                includeUnits: true,
                includeDetails: false,
                includeOpenings: false,
                includeLevels: false,
                new[]
                {
                    new ValidationViewSnapshot(
                        1,
                        "View A",
                        "L1",
                        new[]
                        {
                            new ExportFeatureValidationSnapshot("unit", null, "walkway", 1, true, true),
                            new ExportFeatureValidationSnapshot("unit", "dup-1", "walkway", 2, true, true),
                        }),
                    new ValidationViewSnapshot(
                        2,
                        "View B",
                        "L2",
                        new[]
                        {
                            new ExportFeatureValidationSnapshot("unit", "dup-1", "walkway", 3, true, true),
                        }),
                }));

        Assert.True(result.HasErrors);
        Assert.Contains(result.Issues, issue => issue.Code == ValidationCode.MissingStableId);
        Assert.Contains(result.Issues, issue => issue.Code == ValidationCode.DuplicateStableId);
        Assert.Contains(result.Issues, issue => issue.Code == ValidationCode.MissingStableId && issue.ActionKind == ValidationActionKind.RegenerateStableIds);
        Assert.Contains(result.Issues, issue => issue.Code == ValidationCode.DuplicateStableId && issue.CanNavigateInRevit);
    }

    [Fact]
    public void Validate_ReturnsWarningsForInvalidGeometryUnsupportedOpeningsAndUnsnappedOpenings()
    {
        ExportValidationService service = new();
        ExportValidationResult result = service.Validate(
            CreateRequest(
                6677,
                includeUnits: true,
                includeDetails: false,
                includeOpenings: true,
                includeLevels: false,
                new[]
                {
                    new ValidationViewSnapshot(
                        1,
                        "View A",
                        "L1",
                        new[]
                        {
                            new ExportFeatureValidationSnapshot("unit", "unit-1", "walkway", 10, hasGeometry: false, geometryValid: false),
                            new ExportFeatureValidationSnapshot("opening", "opening-1", "pedestrian", 11, hasGeometry: true, geometryValid: true, isSnappedToOutline: false),
                        },
                        unsupportedOpenings: new[]
                        {
                            new UnsupportedOpeningFamilySnapshot("Odd Door Family", 12),
                        }),
                }));

        Assert.Contains(result.Issues, issue => issue.Code == ValidationCode.EmptyGeometry);
        Assert.Contains(result.Issues, issue => issue.Code == ValidationCode.UnsupportedOpeningFamily);
        Assert.Contains(result.Issues, issue => issue.Code == ValidationCode.UnsnappedOpening);
        Assert.Contains(result.Issues, issue => issue.Code == ValidationCode.UnsupportedOpeningFamily && issue.ActionKind == ValidationActionKind.ReviewOpeningFamilies);
    }

    [Fact]
    public void Validate_ReturnsWarningForLinkedFallbackIdsWithoutNavigation()
    {
        ExportValidationService service = new();
        ExportValidationResult result = service.Validate(
            CreateRequest(
                6677,
                includeUnits: true,
                includeDetails: false,
                includeOpenings: false,
                includeLevels: false,
                new[]
                {
                    new ValidationViewSnapshot(
                        1,
                        "Host View",
                        "L1",
                        new[]
                        {
                            new ExportFeatureValidationSnapshot(
                                "unit",
                                "fallback-1",
                                "retail",
                                501,
                                hasGeometry: true,
                                geometryValid: true,
                                sourceDocumentKey: "LinkedModelA",
                                sourceDocumentName: "Linked Model A",
                                isLinkedSource: true,
                                hasPersistedExportId: false),
                        }),
                }));

        ValidationIssue issue = Assert.Single(result.Issues);
        Assert.Equal(ValidationCode.LinkedElementUsingFallbackId, issue.Code);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Equal("LinkedModelA", issue.SourceDocumentKey);
        Assert.False(issue.CanNavigateInRevit);
        Assert.Equal(ValidationActionKind.ReviewElementInRevit, issue.ActionKind);
    }

    [Fact]
    public void Validate_DoesNotMarkLinkedUnsupportedOpeningsAsNavigable()
    {
        ExportValidationService service = new();
        ExportValidationResult result = service.Validate(
            CreateRequest(
                6677,
                includeUnits: false,
                includeDetails: false,
                includeOpenings: true,
                includeLevels: false,
                new[]
                {
                    new ValidationViewSnapshot(
                        1,
                        "Host View",
                        "L1",
                        new[]
                        {
                            new ExportFeatureValidationSnapshot(
                                "opening",
                                "opening-1",
                                "pedestrian",
                                99,
                                hasGeometry: true,
                                geometryValid: true),
                        },
                        unsupportedOpenings: new[]
                        {
                            new UnsupportedOpeningFamilySnapshot(
                                "Odd Door Family",
                                12,
                                sourceDocumentKey: "LinkedModelB",
                                sourceDocumentName: "Linked Model B",
                                canNavigateInRevit: false),
                        }),
                }));

        ValidationIssue issue = Assert.Single(result.Issues.Where(x => x.Code == ValidationCode.UnsupportedOpeningFamily));
        Assert.Equal(ValidationCode.UnsupportedOpeningFamily, issue.Code);
        Assert.Equal("LinkedModelB", issue.SourceDocumentKey);
        Assert.False(issue.CanNavigateInRevit);
    }

    [Fact]
    public void Validate_ReturnsWarningsForEmptyViewsAndMissingVerticalCirculation()
    {
        ExportValidationService service = new();
        ExportValidationResult result = service.Validate(
            CreateRequest(
                6677,
                includeUnits: true,
                includeDetails: true,
                includeOpenings: true,
                includeLevels: false,
                new[]
                {
                    new ValidationViewSnapshot(
                        1,
                        "Empty View",
                        "L1",
                        Array.Empty<ExportFeatureValidationSnapshot>()),
                    new ValidationViewSnapshot(
                        2,
                        "Vertical View",
                        "L2",
                        new[]
                        {
                            new ExportFeatureValidationSnapshot("unit", "stairs-1", "stairs", 20, true, true),
                        },
                        sourceStairsCount: 2,
                        sourceEscalatorCount: 1,
                        sourceElevatorCount: 1),
                }));

        Assert.Contains(result.Issues, issue => issue.Code == ValidationCode.EmptyViewOutput);
        Assert.Contains(result.Issues, issue => issue.Code == ValidationCode.MissingVerticalCirculation);
        Assert.Contains(result.Issues, issue => issue.Code == ValidationCode.MissingVerticalCirculation && issue.ActionKind == ValidationActionKind.ReviewVerticalCirculation);
    }

    [Fact]
    public void Validate_ReturnsErrorForInvalidTargetEpsg()
    {
        ExportValidationService service = new();
        ExportValidationResult result = service.Validate(
            CreateRequest(
                0,
                includeUnits: false,
                includeDetails: false,
                includeOpenings: false,
                includeLevels: false,
                Array.Empty<ValidationViewSnapshot>()));

        ValidationIssue issue = Assert.Single(result.Issues);
        Assert.Equal(ValidationCode.InvalidTargetEpsg, issue.Code);
        Assert.True(result.HasErrors);
        Assert.Equal(ValidationActionKind.ReviewExportSettings, issue.ActionKind);
        Assert.False(issue.CanNavigateInRevit);
    }

    private static ExportValidationRequest CreateRequest(
        int targetEpsg,
        bool includeUnits,
        bool includeDetails,
        bool includeOpenings,
        bool includeLevels,
        ValidationViewSnapshot[] views)
    {
        return new ExportValidationRequest(
            targetEpsg,
            includeUnits,
            includeDetails,
            includeOpenings,
            includeLevels,
            views,
            UnitSource.Floors,
            "Name",
            "TestModel");
    }
}
