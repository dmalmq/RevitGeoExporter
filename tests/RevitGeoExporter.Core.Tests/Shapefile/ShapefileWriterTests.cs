using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
            AssertShapefileFeatureCount(shapefilePath, 1);
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
            AssertShapefileFeatureCount(shapefilePath, 1);
            Assert.False(File.Exists(Path.Combine(directory, "view(TP-3.shp")));
        }
        finally
        {
            DeleteDirectoryIfExists(directory);
        }
    }

    [Fact]
    public void WritesAllComponents_WhenFirstFeatureHasNullAttribute()
    {
        string directory = CreateTemporaryDirectory();
        string shapefilePath = Path.Combine(directory, "nullable_unit.shp");

        try
        {
            ExportLayer layer = CreateUnitLayer();
            layer.AddFeature(CreateSquareFeature(name: null));

            ShapefileWriter writer = new();
            writer.Write(shapefilePath, srsId: 6677, layers: new[] { layer });

            AssertShapefileSetExists(shapefilePath);
            AssertShapefileFeatureCount(shapefilePath, 1);
        }
        finally
        {
            DeleteDirectoryIfExists(directory);
        }
    }

    [Fact]
    public void WritesUtf8DbfText_WhenAttributeContainsJapaneseText()
    {
        string directory = CreateTemporaryDirectory();
        string shapefilePath = Path.Combine(directory, "utf8_unit.shp");
        string name = "八重洲ユニット";

        try
        {
            ExportLayer layer = CreateUnitLayer();
            layer.AddFeature(CreateSquareFeature(name));

            ShapefileWriter writer = new();
            writer.Write(shapefilePath, srsId: 6677, layers: new[] { layer });

            AssertShapefileSetExists(shapefilePath);
            AssertShapefileFeatureCount(shapefilePath, 1);
            byte[] dbfBytes = File.ReadAllBytes(Path.ChangeExtension(shapefilePath, ".dbf"));
            byte[] expectedBytes = Encoding.UTF8.GetBytes(name);
            Assert.True(
                ContainsSequence(dbfBytes, expectedBytes),
                "Expected DBF text attributes to be encoded as UTF-8.");
        }
        finally
        {
            DeleteDirectoryIfExists(directory);
        }
    }

    [Fact]
    public void CancelledWrite_PreservesExistingComponents()
    {
        string directory = CreateTemporaryDirectory();
        string shapefilePath = Path.Combine(directory, "existing_unit.shp");

        try
        {
            ExportLayer layer = CreateUnitLayer();
            layer.AddFeature(CreateSquareFeature());
            ShapefileWriter writer = new();
            writer.Write(shapefilePath, srsId: 6677, layers: new[] { layer });
            byte[] existingBytes = File.ReadAllBytes(shapefilePath);

            using System.Threading.CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                writer.Write(shapefilePath, srsId: 6677, layers: new[] { layer }, cancellation.Token));

            Assert.Equal(existingBytes, File.ReadAllBytes(shapefilePath));
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
                new AttributeDefinition("name", ExportAttributeType.Text),
                new AttributeDefinition("level_id", ExportAttributeType.Text),
            });
    }

    private static ExportPolygon CreateSquareFeature(string? name = "Unit")
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
                ["name"] = name,
                ["level_id"] = "level-1",
            });
    }

    private static bool ContainsSequence(byte[] bytes, byte[] sequence)
    {
        if (sequence.Length == 0)
        {
            return true;
        }

        for (int i = 0; i <= bytes.Length - sequence.Length; i++)
        {
            bool matches = true;
            for (int j = 0; j < sequence.Length; j++)
            {
                if (bytes[i + j] != sequence[j])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
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

    private static void AssertShapefileFeatureCount(string shapefilePath, int expectedCount)
    {
        Assert.Equal(expectedCount, ReadDbaseRecordCount(Path.ChangeExtension(shapefilePath, ".dbf")));
        Assert.Equal(expectedCount, ReadShapeRecordCount(shapefilePath));
    }

    private static int ReadDbaseRecordCount(string dbfPath)
    {
        byte[] header = File.ReadAllBytes(dbfPath);
        Assert.True(header.Length >= 8, "Expected DBF header to include a record count.");
        return BitConverter.ToInt32(header, 4);
    }

    private static int ReadShapeRecordCount(string shpPath)
    {
        using FileStream stream = File.OpenRead(shpPath);
        Assert.True(stream.Length >= 100, "Expected SHP header to be at least 100 bytes.");
        stream.Position = 100;

        int count = 0;
        byte[] recordHeader = new byte[8];
        while (stream.Position < stream.Length)
        {
            int read = stream.Read(recordHeader, 0, recordHeader.Length);
            Assert.Equal(recordHeader.Length, read);

            int contentLengthWords = ReadBigEndianInt32(recordHeader, 4);
            Assert.True(contentLengthWords >= 0, "Expected SHP record content length to be non-negative.");
            stream.Position += contentLengthWords * 2L;
            count++;
        }

        return count;
    }

    private static int ReadBigEndianInt32(byte[] bytes, int startIndex)
    {
        return (bytes[startIndex] << 24) |
               (bytes[startIndex + 1] << 16) |
               (bytes[startIndex + 2] << 8) |
               bytes[startIndex + 3];
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
