using System.Linq;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Validation;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Validation;

public sealed class ExportReadinessSummaryBuilderTests
{
    [Fact]
    public void Build_SummarizesCountsAndAggregatesMappingSuggestions()
    {
        ExportValidationRequest request = new(
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
                        new ExportFeatureValidationSnapshot(
                            "unit",
                            "unit-1",
                            "retail",
                            101,
                            hasGeometry: true,
                            geometryValid: true,
                            isUnassigned: false,
                            name: "Retail A",
                            altName: "A"),
                        new ExportFeatureValidationSnapshot(
                            "unit",
                            null,
                            "unspecified",
                            102,
                            hasGeometry: true,
                            geometryValid: true,
                            isUnassigned: true,
                            assignmentMappingKey: "Retail (inside fare gates)",
                            assignmentSourceKind: "floor",
                            assignmentParameterName: "Type Name",
                            isSnappedToOutline: true,
                            assignmentParsedCandidate: "Retail (inside fare gates)"),
                    },
                    unsupportedOpenings: new[]
                    {
                        new UnsupportedOpeningFamilySnapshot("Odd Door Family", 5001),
                    }),
                new ValidationViewSnapshot(
                    2,
                    "View B",
                    "L2",
                    new[]
                    {
                        new ExportFeatureValidationSnapshot(
                            "unit",
                            "unit-1",
                            "retail",
                            103,
                            hasGeometry: true,
                            geometryValid: true,
                            isUnassigned: true,
                            assignmentMappingKey: "Retail (inside fare gates)",
                            assignmentSourceKind: "floor",
                            assignmentParameterName: "Type Name",
                            isSnappedToOutline: true,
                            assignmentParsedCandidate: "Retail (inside fare gates)",
                            name: "Retail B"),
                    },
                    unsupportedOpenings: new[]
                    {
                        new UnsupportedOpeningFamilySnapshot("Other Odd Door Family", 5002),
                    }),
            },
            UnitSource.Floors,
            "Name",
            "TestModel");

        ExportValidationResult validationResult = new ExportValidationService().Validate(request);
        ExportReadinessSummary summary = new ExportReadinessSummaryBuilder().Build(
            request,
            validationResult,
            ZoneCatalog.CreateDefault());

        Assert.Equal(3, summary.TotalUnitCount);
        Assert.Equal(2, summary.UnitsWithNameCount);
        Assert.Equal(1, summary.UnitsWithAltNameCount);
        Assert.Equal(1, summary.MissingStableIdCount);
        Assert.Equal(1, summary.DuplicateStableIdCount);
        Assert.Equal(2, summary.UnsupportedOpeningFamilyCount);
        Assert.Equal(2, summary.UnmappedAssignmentCount);
        Assert.Equal(2, summary.BlockingIssueCount);
        Assert.True(summary.WarningIssueCount >= 4);

        ValidationMappingSuggestion suggestion = Assert.Single(summary.MappingSuggestions);
        Assert.Equal("floor", suggestion.SourceKind);
        Assert.Equal("Retail (inside fare gates)", suggestion.MappingKey);
        Assert.Equal(2, suggestion.OccurrenceCount);
        Assert.Equal("Type Name", suggestion.ParameterName);
        Assert.Equal("retail", suggestion.SuggestedCategory);
    }

    [Fact]
    public void Build_FallsBackToMappingKeyWhenParsedCandidateIsMissing()
    {
        ExportValidationRequest request = new(
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
                            assignmentMappingKey: "office",
                            assignmentSourceKind: "room",
                            assignmentParameterName: "Department"),
                    }),
            },
            UnitSource.Rooms,
            "Department",
            "TestModel");

        ExportValidationResult validationResult = new ExportValidationService().Validate(request);
        ExportReadinessSummary summary = new ExportReadinessSummaryBuilder().Build(
            request,
            validationResult,
            ZoneCatalog.CreateDefault());

        ValidationMappingSuggestion suggestion = Assert.Single(summary.MappingSuggestions);
        Assert.Equal("office", suggestion.MappingKey);
        Assert.Equal("office", suggestion.SuggestedCategory);
    }
}
