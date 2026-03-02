using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.GeoPackage;

public sealed class GpkgWriter
{
    public const string DefaultGeometryColumn = "geom";

    static GpkgWriter()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public void Write(string filePath, int srsId, IReadOnlyCollection<ExportLayer> layers)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Output path is required.", nameof(filePath));
        }

        if (layers is null)
        {
            throw new ArgumentNullException(nameof(layers));
        }

        if (layers.Count == 0)
        {
            throw new ArgumentException("At least one layer is required.", nameof(layers));
        }

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        using SqliteConnection connection = new($"Data Source={filePath};Pooling=False");
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        GpkgSchema.EnsureCoreTables(connection, transaction);
        GpkgSchema.EnsureSpatialReference(connection, transaction, srsId);

        foreach (ExportLayer layer in layers)
        {
            CreateLayerTable(connection, transaction, layer);
            InsertLayerMetadata(connection, transaction, layer, srsId);
            InsertFeatures(connection, transaction, layer, srsId);
        }

        transaction.Commit();
    }

    private static void CreateLayerTable(SqliteConnection connection, SqliteTransaction transaction, ExportLayer layer)
    {
        string tableName = QuoteIdentifier(layer.Name);
        string geometryName = QuoteIdentifier(DefaultGeometryColumn);
        StringBuilder sql = new();
        sql.Append("CREATE TABLE ");
        sql.Append(tableName);
        sql.Append(" (");
        sql.Append("\"fid\" INTEGER PRIMARY KEY AUTOINCREMENT, ");
        sql.Append(geometryName);
        sql.Append(" BLOB NOT NULL");

        foreach (AttributeDefinition attribute in layer.Attributes)
        {
            sql.Append(", ");
            sql.Append(QuoteIdentifier(attribute.Name));
            sql.Append(' ');
            sql.Append(GetSqlType(attribute.Type));
        }

        sql.Append(");");
        ExecuteNonQuery(connection, transaction, sql.ToString());
    }

    private static void InsertLayerMetadata(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ExportLayer layer,
        int srsId)
    {
        LayerBounds? bounds = CalculateBounds(layer.Features);
        string utcNow = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        using SqliteCommand contents = connection.CreateCommand();
        contents.Transaction = transaction;
        contents.CommandText =
            @"INSERT INTO gpkg_contents
              (table_name, data_type, identifier, description, last_change, min_x, min_y, max_x, max_y, srs_id)
              VALUES ($table_name, 'features', $identifier, '', $last_change, $min_x, $min_y, $max_x, $max_y, $srs_id);";
        contents.Parameters.AddWithValue("$table_name", layer.Name);
        contents.Parameters.AddWithValue("$identifier", layer.Name);
        contents.Parameters.AddWithValue("$last_change", utcNow);
        contents.Parameters.AddWithValue("$min_x", bounds?.MinX ?? (object)DBNull.Value);
        contents.Parameters.AddWithValue("$min_y", bounds?.MinY ?? (object)DBNull.Value);
        contents.Parameters.AddWithValue("$max_x", bounds?.MaxX ?? (object)DBNull.Value);
        contents.Parameters.AddWithValue("$max_y", bounds?.MaxY ?? (object)DBNull.Value);
        contents.Parameters.AddWithValue("$srs_id", srsId);
        contents.ExecuteNonQuery();

        using SqliteCommand columns = connection.CreateCommand();
        columns.Transaction = transaction;
        columns.CommandText =
            @"INSERT INTO gpkg_geometry_columns
              (table_name, column_name, geometry_type_name, srs_id, z, m)
              VALUES ($table_name, $column_name, $geometry_type_name, $srs_id, 0, 0);";
        columns.Parameters.AddWithValue("$table_name", layer.Name);
        columns.Parameters.AddWithValue("$column_name", DefaultGeometryColumn);
        columns.Parameters.AddWithValue("$geometry_type_name", GetGeometryTypeName(layer.GeometryType));
        columns.Parameters.AddWithValue("$srs_id", srsId);
        columns.ExecuteNonQuery();
    }

    private static void InsertFeatures(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ExportLayer layer,
        int srsId)
    {
        if (layer.Features.Count == 0)
        {
            return;
        }

        string tableName = QuoteIdentifier(layer.Name);
        StringBuilder columns = new();
        StringBuilder parameters = new();
        columns.Append(QuoteIdentifier(DefaultGeometryColumn));
        parameters.Append("$geom");

        string[] valueParameterNames = new string[layer.Attributes.Count];
        for (int i = 0; i < layer.Attributes.Count; i++)
        {
            columns.Append(", ");
            columns.Append(QuoteIdentifier(layer.Attributes[i].Name));
            string param = $"$p{i}";
            valueParameterNames[i] = param;
            parameters.Append(", ");
            parameters.Append(param);
        }

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {tableName} ({columns}) VALUES ({parameters});";

        SqliteParameter geometryParameter = command.CreateParameter();
        geometryParameter.ParameterName = "$geom";
        geometryParameter.SqliteType = SqliteType.Blob;
        command.Parameters.Add(geometryParameter);

        for (int i = 0; i < valueParameterNames.Length; i++)
        {
            SqliteParameter parameter = command.CreateParameter();
            parameter.ParameterName = valueParameterNames[i];
            parameter.Value = DBNull.Value;
            command.Parameters.Add(parameter);
        }

        foreach (IExportFeature feature in layer.Features)
        {
            geometryParameter.Value = EncodeGeometry(layer.GeometryType, feature, srsId);

            for (int i = 0; i < layer.Attributes.Count; i++)
            {
                string attributeName = layer.Attributes[i].Name;
                object? value = feature.Attributes.TryGetValue(attributeName, out object? attributeValue)
                    ? attributeValue
                    : null;
                command.Parameters[valueParameterNames[i]].Value = ToDbValue(value);
            }

            command.ExecuteNonQuery();
        }
    }

    private static byte[] EncodeGeometry(GpkgGeometryType geometryType, IExportFeature feature, int srsId)
    {
        byte[] wkb = geometryType switch
        {
            GpkgGeometryType.Polygon when feature is ExportPolygon polygonFeature &&
                                          polygonFeature.Polygons.Count == 1 =>
                WkbEncoder.EncodePolygon(polygonFeature.Polygons[0]),
            GpkgGeometryType.MultiPolygon when feature is ExportPolygon multiPolygonFeature =>
                WkbEncoder.EncodeMultiPolygon(multiPolygonFeature.Polygons),
            GpkgGeometryType.LineString when feature is ExportLineString lineFeature =>
                WkbEncoder.EncodeLineString(lineFeature.LineString),
            GpkgGeometryType.Polygon => throw new InvalidOperationException(
                "Polygon layer features must contain exactly one polygon geometry."),
            GpkgGeometryType.MultiPolygon => throw new InvalidOperationException(
                "MultiPolygon layer features require polygon geometry."),
            GpkgGeometryType.LineString => throw new InvalidOperationException(
                "LineString layer features require line geometry."),
            _ => throw new NotSupportedException($"Geometry type '{geometryType}' is not supported."),
        };

        return WkbEncoder.WrapInGeoPackageHeader(wkb, srsId);
    }

    private static string GetSqlType(ExportAttributeType attributeType)
    {
        return attributeType switch
        {
            ExportAttributeType.Integer => "INTEGER",
            ExportAttributeType.Real => "REAL",
            ExportAttributeType.Text => "TEXT",
            ExportAttributeType.Boolean => "INTEGER",
            _ => throw new ArgumentOutOfRangeException(nameof(attributeType), attributeType, null),
        };
    }

    private static object ToDbValue(object? value)
    {
        if (value is null)
        {
            return DBNull.Value;
        }

        return value switch
        {
            bool b => b ? 1 : 0,
            Enum e => e.ToString(),
            _ => value,
        };
    }

    private static string GetGeometryTypeName(GpkgGeometryType geometryType)
    {
        return geometryType switch
        {
            GpkgGeometryType.Point => "POINT",
            GpkgGeometryType.LineString => "LINESTRING",
            GpkgGeometryType.Polygon => "POLYGON",
            GpkgGeometryType.MultiPolygon => "MULTIPOLYGON",
            _ => throw new ArgumentOutOfRangeException(nameof(geometryType), geometryType, null),
        };
    }

    private static LayerBounds? CalculateBounds(IReadOnlyList<IExportFeature> features)
    {
        bool hasAnyPoint = false;
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        foreach (IExportFeature feature in features)
        {
            foreach (Point2D point in feature.GetAllPoints())
            {
                hasAnyPoint = true;
                if (point.X < minX)
                {
                    minX = point.X;
                }

                if (point.Y < minY)
                {
                    minY = point.Y;
                }

                if (point.X > maxX)
                {
                    maxX = point.X;
                }

                if (point.Y > maxY)
                {
                    maxY = point.Y;
                }
            }
        }

        return hasAnyPoint ? new LayerBounds(minX, minY, maxX, maxY) : null;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class LayerBounds
    {
        public LayerBounds(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public double MinX { get; }

        public double MinY { get; }

        public double MaxX { get; }

        public double MaxY { get; }
    }
}
