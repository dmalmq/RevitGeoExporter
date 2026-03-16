using System;
using RevitGeoExporter.Core.Diagnostics;
using RevitGeoExporter.Core.Validation;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Diagnostics;

public sealed class ChangeSummaryServiceTests
{
    [Fact]
    public void Compare_WhenNoBaseline_ReturnsFirstBaselineMessage()
    {
        ChangeSummaryService service = new();
        ExportDiagnosticsReport currentReport = CreateReport(3, 1);
        ExportPackageManifest currentManifest = CreateManifest("unit.gpkg");

        ExportChangeSummary summary = service.Compare(null, currentReport, null, currentManifest);

        Assert.Single(summary.Lines);
        Assert.Contains("baseline", summary.Lines[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compare_WhenCountsDiffer_ReportsChanges()
    {
        ChangeSummaryService service = new();
        ExportDiagnosticsReport previousReport = CreateReport(3, 1);
        ExportDiagnosticsReport currentReport = CreateReport(5, 2);
        ExportPackageManifest previousManifest = CreateManifest("unit.gpkg");
        ExportPackageManifest currentManifest = CreateManifest("unit.gpkg", "opening.gpkg");

        ExportChangeSummary summary = service.Compare(previousReport, currentReport, previousManifest, currentManifest);

        Assert.Contains(summary.Lines, line => line.Contains("Layer count changed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(summary.Lines, line => line.Contains("Warning count changed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(summary.Lines, line => line.Contains("Output file added", StringComparison.OrdinalIgnoreCase));
    }

    private static ExportDiagnosticsReport CreateReport(int unitCount, int warningCount)
    {
        ExportDiagnosticsReport report = new()
        {
            SourceModelName = "Model",
            TargetEpsg = 6677,
            ExportedAtUtc = DateTimeOffset.UtcNow,
            Views =
            {
                new ExportDiagnosticsViewReport
                {
                    ViewId = 1,
                    ViewName = "Level 1",
                    LevelName = "L1",
                    Layers =
                    {
                        new ExportDiagnosticsLayerCount
                        {
                            FeatureType = "unit",
                            Category = "retail",
                            Count = unitCount,
                        },
                    },
                },
            },
        };
        for (int i = 0; i < warningCount; i++)
        {
            report.ExportWarnings.Add($"warn-{i + 1}");
        }

        return report;
    }

    private static ExportPackageManifest CreateManifest(params string[] fileNames)
    {
        ExportPackageManifest manifest = new()
        {
            SourceModelName = "Model",
            PackageDirectory = "package",
            TargetEpsg = 6677,
            ExportedAtUtc = DateTimeOffset.UtcNow,
        };
        foreach (string fileName in fileNames)
        {
            manifest.Files.Add(new ExportPackageManifestFile
            {
                Kind = "gpkg",
                RelativePath = fileName,
            });
        }

        return manifest;
    }
}
