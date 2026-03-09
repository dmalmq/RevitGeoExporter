using System;
using System.IO;
using System.Linq;
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
            new ExportValidationRequest(
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
                                isUnassignedFloor: true,
                                floorTypeName: "Wrong Floor Name"),
                        }),
                }));

        ValidationIssue issue = Assert.Single(result.Issues);
        Assert.Equal(ValidationCode.UnassignedFloorCategory, issue.Code);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
    }

    [Fact]
    public void Validate_ReturnsErrorForMissingOrDuplicateStableIds()
    {
        ExportValidationService service = new();
        ExportValidationResult result = service.Validate(
            new ExportValidationRequest(
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
    }

    [Fact]
    public void Validate_ReturnsWarningsForInvalidGeometryUnsupportedOpeningsAndUnsnappedOpenings()
    {
        ExportValidationService service = new();
        ExportValidationResult result = service.Validate(
            new ExportValidationRequest(
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
    }

    [Fact]
    public void Validate_ReturnsWarningsForEmptyViewsAndMissingVerticalCirculation()
    {
        ExportValidationService service = new();
        ExportValidationResult result = service.Validate(
            new ExportValidationRequest(
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
    }

    [Fact]
    public void Validate_ReturnsErrorForInvalidTargetEpsg()
    {
        ExportValidationService service = new();
        ExportValidationResult result = service.Validate(
            new ExportValidationRequest(
                0,
                includeUnits: false,
                includeDetails: false,
                includeOpenings: false,
                includeLevels: false,
                Array.Empty<ValidationViewSnapshot>()));

        ValidationIssue issue = Assert.Single(result.Issues);
        Assert.Equal(ValidationCode.InvalidTargetEpsg, issue.Code);
        Assert.True(result.HasErrors);
    }
}
