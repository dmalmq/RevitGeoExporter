using System;
using System.Collections.Generic;
using System.IO;
using RevitGeoExporter.Core.GeoPackage;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Shapefile;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Shapefile;

public sealed class ShapefileWriterTests
{
    [Fact]
    public void WritesAllComponents_WhenPathIncludesShapefileExtensionAndPeriods()
    {
        string directory = CreateTemporaryDirectory();
        string shapefilePath = Path.Combine(directory, "トフロム八重洲_B2FL_トフロム八重洲(TP-3.35)_unit.shp");

        try
        {
            ExportLayer layer = CreateUnitLayer();
            layer.AddFeature(CreateSquareFeature());

            ShapefileWriter writer = new();
            writer.Write(shapefilePath, srsId: 6677, layers: new[] { layer });

            AssertShapefileSetExists(shapefilePath);
        }
        finally
        {
            DeleteDirectoryIfExists(directory);
        }
    }

    [Fact]
    public void WritesAllComponents_WhenPathOmitsShapefileExtensionAndPeriods()
    {
        string directory = CreateTemporaryDirectory();
        string basePath = Path.Combine(directory, "view(TP-3.35)_unit");
        string shapefilePath = basePath + ".shp";

        try
        {
            ExportLayer layer = CreateUnitLayer();
            layer.AddFeature(CreateSquareFeature());

            ShapefileWriter writer = new();
            writer.Write(basePath, srsId: 6677, layers: new[] { layer });

            AssertShapefileSetExists(shapefilePath);
            Assert.False(File.Exists(Path.Combine(directory, "view(TP-3.shp")));
        }
        finally
        {
            DeleteDirectoryIfExists(directory);
        }
    }

    private static ExportLayer CreateUnitLayer()
    {
        return new ExportLayer(
            name: "unit",
            geometryType: GpkgGeometryType.MultiPolygon,
            attributes: new[]
            {
                new AttributeDefinition("id", ExportAttributeType.Text),
                new AttributeDefinition("category", ExportAttributeType.Text),
                new AttributeDefinition("level_id", ExportAttributeType.Text),
            });
    }

    private static ExportPolygon CreateSquareFeature()
    {
        Polygon2D geometry = new(
            new[]
            {
                new Point2D(0, 0),
                new Point2D(5, 0),
                new Point2D(5, 5),
                new Point2D(0, 5),
            });

        return new ExportPolygon(
            geometry,
            new Dictionary<string, object?>
            {
                ["id"] = Guid.NewGuid().ToString(),
                ["category"] = "walkway",
                ["level_id"] = "level-1",
            });
    }

    private static void AssertShapefileSetExists(string shapefilePath)
    {
        string[] expectedPaths =
        {
            shapefilePath,
            Path.ChangeExtension(shapefilePath, ".shx"),
            Path.ChangeExtension(shapefilePath, ".dbf"),
            Path.ChangeExtension(shapefilePath, ".prj"),
            Path.ChangeExtension(shapefilePath, ".cpg"),
        };

        foreach (string expectedPath in expectedPaths)
        {
            Assert.True(File.Exists(expectedPath), $"Expected shapefile component to exist: {expectedPath}");
        }

        Assert.Equal("UTF-8", File.ReadAllText(Path.ChangeExtension(shapefilePath, ".cpg")).Trim());
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "RevitGeoExporter-ShapefileTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
