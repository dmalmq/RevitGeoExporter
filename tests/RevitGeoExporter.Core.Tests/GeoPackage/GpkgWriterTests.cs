using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using RevitGeoExporter.Core.GeoPackage;
using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.GeoPackage;

public sealed class GpkgWriterTests
{
    [Fact]
    public void CreatesValidFile_WithExpectedMetadataAndFeature()
    {
        string path = GetTemporaryGpkgPath();
        try
        {
            ExportLayer unit = CreateUnitLayer();
            unit.AddFeature(CreateSquareFeature("unit-1", "walkway", "rachi_nai", "Label A"));

            GpkgWriter writer = new();
            writer.Write(path, srsId: 6677, layers: new[] { unit });

            Assert.True(File.Exists(path));

            using SqliteConnection connection = new($"Data Source={path};Pooling=False");
            connection.Open();

            Assert.Equal(1L, ExecuteScalarLong(connection, "SELECT COUNT(*) FROM gpkg_spatial_ref_sys WHERE srs_id = 6677;"));
            Assert.Equal(1L, ExecuteScalarLong(connection, "SELECT COUNT(*) FROM gpkg_contents WHERE table_name = 'unit';"));
            Assert.Equal(1L, ExecuteScalarLong(connection, "SELECT COUNT(*) FROM gpkg_geometry_columns WHERE table_name = 'unit';"));
            Assert.Equal(1L, ExecuteScalarLong(connection, "SELECT COUNT(*) FROM unit;"));
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public void MultipleFeatures_ArePersisted()
    {
        string path = GetTemporaryGpkgPath();
        try
        {
            ExportLayer unit = CreateUnitLayer();
            for (int i = 0; i < 10; i++)
            {
                unit.AddFeature(
                    CreateSquareFeature(
                        id: $"unit-{i}",
                        category: "walkway",
                        restrict: i % 2 == 0 ? "rachi_nai" : null,
                        name: null));
            }

            GpkgWriter writer = new();
            writer.Write(path, srsId: 6677, layers: new[] { unit });

            using SqliteConnection connection = new($"Data Source={path};Pooling=False");
            connection.Open();

            Assert.Equal(10L, ExecuteScalarLong(connection, "SELECT COUNT(*) FROM unit;"));
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public void UnitSchema_ContainsAllRequiredColumns()
    {
        string path = GetTemporaryGpkgPath();
        try
        {
            ExportLayer unit = CreateUnitLayer();
            unit.AddFeature(CreateSquareFeature("unit-1", "walkway", null, null));
            GpkgWriter writer = new();
            writer.Write(path, srsId: 6677, layers: new[] { unit });

            using SqliteConnection connection = new($"Data Source={path};Pooling=False");
            connection.Open();
            HashSet<string> columns = GetTableColumns(connection, "unit");

            Assert.Contains("fid", columns);
            Assert.Contains("geom", columns);
            Assert.Contains("id", columns);
            Assert.Contains("category", columns);
            Assert.Contains("restrict", columns);
            Assert.Contains("name", columns);
            Assert.Contains("alt_name", columns);
            Assert.Contains("level_id", columns);
            Assert.Contains("source", columns);
            Assert.Contains("display_point", columns);
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public void NullableFields_ArePersistedAsNull()
    {
        string path = GetTemporaryGpkgPath();
        try
        {
            ExportLayer unit = CreateUnitLayer();
            unit.AddFeature(CreateSquareFeature("unit-1", "walkway", null, null));

            GpkgWriter writer = new();
            writer.Write(path, srsId: 6677, layers: new[] { unit });

            using SqliteConnection connection = new($"Data Source={path};Pooling=False");
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT restrict, name, alt_name FROM unit LIMIT 1;";
            using SqliteDataReader reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.True(reader.IsDBNull(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.IsDBNull(2));
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public void LineStringLayer_WritesExpectedGeometryMetadata()
    {
        string path = GetTemporaryGpkgPath();
        try
        {
            ExportLayer detail = new(
                name: "detail",
                geometryType: GpkgGeometryType.LineString,
                attributes: new[]
                {
                    new AttributeDefinition("id", ExportAttributeType.Text),
                    new AttributeDefinition("level_id", ExportAttributeType.Text),
                    new AttributeDefinition("element_id", ExportAttributeType.Integer),
                });

            detail.AddFeature(
                new ExportLineString(
                    new LineString2D(
                        new[]
                        {
                            new Point2D(0, 0),
                            new Point2D(10, 5),
                        }),
                    new Dictionary<string, object?>
                    {
                        ["id"] = Guid.NewGuid().ToString(),
                        ["level_id"] = Guid.NewGuid().ToString(),
                        ["element_id"] = 12345L,
                    }));

            GpkgWriter writer = new();
            writer.Write(path, srsId: 6677, layers: new[] { detail });

            using SqliteConnection connection = new($"Data Source={path};Pooling=False");
            connection.Open();

            Assert.Equal(
                "LINESTRING",
                ExecuteScalarString(
                    connection,
                    "SELECT geometry_type_name FROM gpkg_geometry_columns WHERE table_name = 'detail';"));
            Assert.Equal(1L, ExecuteScalarLong(connection, "SELECT COUNT(*) FROM detail;"));
        }
        finally
        {
            DeleteIfExists(path);
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
                new AttributeDefinition("restrict", ExportAttributeType.Text),
                new AttributeDefinition("name", ExportAttributeType.Text),
                new AttributeDefinition("alt_name", ExportAttributeType.Text),
                new AttributeDefinition("level_id", ExportAttributeType.Text),
                new AttributeDefinition("source", ExportAttributeType.Text),
                new AttributeDefinition("display_point", ExportAttributeType.Text),
            });
    }

    private static ExportPolygon CreateSquareFeature(
        string id,
        string category,
        string? restrict,
        string? name)
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
                ["id"] = id,
                ["category"] = category,
                ["restrict"] = restrict,
                ["name"] = name,
                ["alt_name"] = null,
                ["level_id"] = Guid.NewGuid().ToString(),
                ["source"] = "TestModel",
                ["display_point"] = "POINT (2.5 2.5)",
            });
    }

    private static long ExecuteScalarLong(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    private static string ExecuteScalarString(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)(command.ExecuteScalar() ?? string.Empty);
    }

    private static HashSet<string> GetTableColumns(SqliteConnection connection, string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using SqliteDataReader reader = command.ExecuteReader();

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }

    private static string GetTemporaryGpkgPath()
    {
        return Path.Combine(Path.GetTempPath(), $"rge-{Guid.NewGuid():N}.gpkg");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
