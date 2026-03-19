using System;
using System.IO;
using Newtonsoft.Json.Linq;
using RevitGeoExporter.Core.Diagnostics;
using RevitGeoExporter.Core.Validation;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Diagnostics;

public sealed class ExportDiagnosticsWriterTests
{
    [Fact]
    public void WriteJson_WritesExpectedReport()
    {
        string tempDirectory = CreateTempDirectory();
        try
        {
            ExportDiagnosticsReport report = new()
            {
                SourceModelName = "TestModel",
                TargetEpsg = 6677,
                ExportedAtUtc = new DateTimeOffset(2026, 3, 9, 0, 0, 0, TimeSpan.Zero),
                DurationMilliseconds = 1234,
                Views =
                {
                    new ExportDiagnosticsViewReport
                    {
                        ViewId = 1,
                        ViewName = "Level 1",
                        LevelName = "L1",
                        UnsnappedOpeningCount = 2,
                        Layers =
                        {
                            new ExportDiagnosticsLayerCount
                            {
                                FeatureType = "unit",
                                Category = "walkway",
                                Count = 5,
                            },
                        },
                        AppliedFloorOverrides =
                        {
                            new ExportDiagnosticsFloorOverride
                            {
                                FloorTypeName = "Wrong Floor Name",
                                Category = "walkway",
                                Count = 3,
                            },
                        },
                        UnassignedFloorTypes =
                        {
                            new ExportDiagnosticsUnassignedFloorGroup
                            {
                                FloorTypeName = "Bad Floor Name",
                                Count = 1,
                            },
                        },
                    },
                },
                ValidationIssues =
                {
                    new ValidationIssue(
                        ValidationSeverity.Warning,
                        ValidationCode.UnassignedFloorCategory,
                        "Floor is unassigned.",
                        "Level 1",
                        "L1",
                        "unit"),
                },
                ExportWarnings = { "Example export warning" },
                IncludedLinks =
                {
                    ExportLinkedModelInfo.Create(101, "Architectural Link", "LinkModelA", "Architectural.rvt"),
                },
                OutputFiles =
                {
                    new ExportDiagnosticsOutputFile
                    {
                        ViewName = "Level 1",
                        FeatureType = "unit",
                        Path = @"C:\temp\level1_unit.gpkg",
                        FeatureCount = 5,
                    },
                },
            };

            ExportDiagnosticsWriter writer = new();
            string path = writer.WriteJson(tempDirectory, report);

            Assert.True(File.Exists(path));

            JObject reloaded = JObject.Parse(File.ReadAllText(path));
            Assert.Equal("TestModel", reloaded["SourceModelName"]?.Value<string>());
            Assert.Equal(6677, reloaded["TargetEpsg"]?.Value<int>());
            Assert.Equal(2, reloaded["Views"]?[0]?["UnsnappedOpeningCount"]?.Value<int>());
            Assert.Equal((int)ValidationCode.UnassignedFloorCategory, reloaded["ValidationIssues"]?[0]?["Code"]?.Value<int>());
            Assert.Equal("Architectural Link", reloaded["IncludedLinks"]?[0]?["LinkInstanceName"]?.Value<string>());
            Assert.Equal("unit", reloaded["OutputFiles"]?[0]?["FeatureType"]?.Value<string>());
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"RevitGeoExporter-DiagnosticsTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
