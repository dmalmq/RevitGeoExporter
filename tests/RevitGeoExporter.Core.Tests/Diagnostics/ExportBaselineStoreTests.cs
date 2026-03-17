using System;
using System.IO;
using RevitGeoExporter.Core.Diagnostics;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Diagnostics;

public sealed class ExportBaselineStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsDiagnosticsManifestAndSnapshot()
    {
        string rootDirectory = CreateTempDirectory();
        try
        {
            ExportBaselineStore store = new(rootDirectory);
            ExportDiagnosticsReport report = new()
            {
                SourceModelName = "Model",
                OutputFiles = { new ExportDiagnosticsOutputFile { FeatureType = "unit", Path = "unit.gpkg" } },
            };
            ExportPackageManifest manifest = new()
            {
                SourceModelName = "Model",
                Files = { new ExportPackageManifestFile { RelativePath = "unit.gpkg", Kind = "gpkg", IsArtifact = true } },
            };
            ExportBaselineSnapshot snapshot = new()
            {
                BaselineKey = "project__profile",
                ConfigurationFingerprint = "abc123",
                Views = { new ExportBaselineViewSnapshot { ViewId = 1, ViewName = "Level 1", ContentFingerprint = "view-hash" } },
            };

            store.Save("project__profile", report, manifest, snapshot);
            ExportBaselineLoadResult loaded = store.Load("project__profile");

            Assert.NotNull(loaded.Report);
            Assert.NotNull(loaded.Manifest);
            Assert.NotNull(loaded.Snapshot);
            Assert.Equal("abc123", loaded.Snapshot!.ConfigurationFingerprint);
            Assert.Equal("unit.gpkg", loaded.Manifest!.Files[0].RelativePath);
            Assert.Equal("unit", loaded.Report!.OutputFiles[0].FeatureType);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"RevitGeoExporter-BaselineTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
