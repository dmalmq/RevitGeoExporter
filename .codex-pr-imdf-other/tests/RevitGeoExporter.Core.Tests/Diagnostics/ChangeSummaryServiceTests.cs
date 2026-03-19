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

        ExportChangeSummary summary = service.Compare(
            null,
            null,
            currentReport,
            null,
            currentManifest,
            changedViewCount: 1,
            reusedViewCount: 0,
            writtenArtifactCount: 1,
            reusedArtifactCount: 0,
            missingBaselineArtifactCount: 0,
            fullRewriteReason: "No previous baseline.");

        Assert.Equal(ExportBaselineStatus.Unavailable, summary.BaselineStatus);
        Assert.Contains(summary.Lines, line => line.Contains("baseline", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_WhenCountsDiffer_ReportsChanges()
    {
        ChangeSummaryService service = new();
        ExportDiagnosticsReport previousReport = CreateReport(3, 1);
        ExportDiagnosticsReport currentReport = CreateReport(5, 2);
        ExportPackageManifest previousManifest = CreateManifest("unit.gpkg");
        ExportPackageManifest currentManifest = CreateManifest("unit.gpkg", "opening.gpkg");
        ExportBaselineSnapshot previousSnapshot = new()
        {
            ConfigurationFingerprint = "same",
            Views = { new ExportBaselineViewSnapshot { ViewId = 1, ViewName = "Level 1", ContentFingerprint = "abc" } },
        };

        ExportChangeSummary summary = service.Compare(
            previousSnapshot,
            previousReport,
            currentReport,
            previousManifest,
            currentManifest,
            changedViewCount: 1,
            reusedViewCount: 0,
            writtenArtifactCount: 2,
            reusedArtifactCount: 0,
            missingBaselineArtifactCount: 0,
            fullRewriteReason: null);

        Assert.Contains(summary.Lines, line => line.Contains("Layer count changed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(summary.Lines, line => line.Contains("Warning count changed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(summary.Lines, line => line.Contains("Output file added", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, summary.ChangedViewCount);
        Assert.Equal(2, summary.WrittenArtifactCount);
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
