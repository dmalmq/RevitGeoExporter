using System;
using System.IO;
using Microsoft.Data.Sqlite;
using RevitGeoExporter.Core.Diagnostics;
using RevitGeoExporter.Core.GeoPackage;
using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Diagnostics;

public sealed class PackageValidationServiceTests
{
    [Fact]
    public void Validate_WhenArtifactMatchesManifest_ReturnsNoIssues()
    {
        string path = GetTemporaryGpkgPath();
        try
        {
            ExportLayer layer = CreateLayer();
            layer.AddFeature(CreateFeature());
            new GpkgWriter().Write(path, 6677, new[] { layer });

            ExportPackageManifest manifest = new()
            {
                Files =
                {
                    new ExportPackageManifestFile
                    {
                        ArtifactKey = "artifact-1",
                        RelativePath = Path.GetFileName(path),
                        OutputFilePath = path,
                        Kind = "gpkg",
                        ContainedLayers = { "unit" },
                        MandatoryLayers = { "unit" },
                        IsArtifact = true,
                    },
                },
            };

            PackageValidationResult result = new PackageValidationService().Validate(manifest);

            Assert.Empty(result.Issues);
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public void Validate_WhenMandatoryLayerIsMissing_ReturnsError()
    {
        string path = GetTemporaryGpkgPath();
        try
        {
            ExportLayer layer = CreateLayer();
            new GpkgWriter().Write(path, 6677, new[] { layer });

            ExportPackageManifest manifest = new()
            {
                Files =
                {
                    new ExportPackageManifestFile
                    {
                        ArtifactKey = "artifact-1",
                        RelativePath = Path.GetFileName(path),
                        OutputFilePath = path,
                        Kind = "gpkg",
                        ContainedLayers = { "opening" },
                        MandatoryLayers = { "opening" },
                        IsArtifact = true,
                    },
                },
            };

            PackageValidationResult result = new PackageValidationService().Validate(manifest);

            Assert.Contains(result.Issues, issue => issue.Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    private static ExportLayer CreateLayer()
    {
        return new ExportLayer(
            "unit",
            GpkgGeometryType.MultiPolygon,
            new[]
            {
                new AttributeDefinition("id", ExportAttributeType.Text),
            });
    }

    private static ExportPolygon CreateFeature()
    {
        return new ExportPolygon(
            new Polygon2D(
                new[]
                {
                    new Point2D(0, 0),
                    new Point2D(5, 0),
                    new Point2D(5, 5),
                    new Point2D(0, 5),
                }),
            new System.Collections.Generic.Dictionary<string, object?>
            {
                ["id"] = "feature-1",
            });
    }

    private static string GetTemporaryGpkgPath()
    {
        return Path.Combine(Path.GetTempPath(), $"rge-validation-{Guid.NewGuid():N}.gpkg");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
