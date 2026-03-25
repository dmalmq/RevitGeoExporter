using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.GeoPackage;
using RevitGeoExporter.Core.Models;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace RevitGeoExporter.Core.Shapefile;

public sealed class ShapefileWriter
{
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 0);

    public void Write(string shapefilePath, int srsId, IReadOnlyCollection<ExportLayer> layers)
    {
        if (string.IsNullOrWhiteSpace(shapefilePath))
        {
            throw new ArgumentException("Shapefile path is required.", nameof(shapefilePath));
        }

        if (layers is null)
        {
            throw new ArgumentNullException(nameof(layers));
        }

        string normalizedShapefilePath = NormalizeShapefilePath(shapefilePath);
        foreach (ExportLayer layer in layers)
        {
            if (layer.Features.Count == 0)
            {
                continue;
            }

            string layerPath = layers.Count == 1
                ? normalizedShapefilePath
                : BuildLayerPath(normalizedShapefilePath, layer.Name);
            WriteLayer(layerPath, srsId, layer);
        }
    }

    private static void WriteLayer(string shapefilePath, int srsId, ExportLayer layer)
    {
        string directory = Path.GetDirectoryName(shapefilePath) ?? string.Empty;
        if (directory.Length > 0 && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Dictionary<string, string> columnNameMap = BuildColumnNameMap(layer.Attributes);
        IList<IFeature> features = new List<IFeature>();

        foreach (IExportFeature exportFeature in layer.Features)
        {
            NtsGeometry? geometry = ConvertGeometry(exportFeature, layer.GeometryType);
            if (geometry == null)
            {
                continue;
            }

            AttributesTable attributes = new();
            foreach (AttributeDefinition attrDef in layer.Attributes)
            {
                string shpName = columnNameMap[attrDef.Name];
                exportFeature.Attributes.TryGetValue(attrDef.Name, out object? value);
                attributes.Add(shpName, CoerceAttributeValue(value, attrDef.Type));
            }

            features.Add(new Feature(geometry, attributes));
        }

        if (features.Count == 0)
        {
            return;
        }

        ShapefileDataWriter writer = new(shapefilePath, GeometryFactory);
        DbaseFileHeader header = ShapefileDataWriter.GetHeader(
            (Feature)features[0],
            features.Count);
        writer.Header = header;
        writer.Write(features);

        WritePrjFile(shapefilePath, srsId);
        WriteCpgFile(shapefilePath);
    }

    private static NtsGeometry? ConvertGeometry(IExportFeature feature, GpkgGeometryType geometryType)
    {
        if (feature is ExportPolygon polygon)
        {
            Polygon[] polygons = polygon.Polygons
                .Select(ConvertPolygon)
                .Where(p => p != null)
                .Cast<Polygon>()
                .ToArray();

            if (polygons.Length == 0)
            {
                return null;
            }

            return geometryType == GpkgGeometryType.MultiPolygon
                ? (NtsGeometry)GeometryFactory.CreateMultiPolygon(polygons)
                : polygons[0];
        }

        if (feature is ExportLineString lineString)
        {
            Coordinate[] coords = lineString.LineString.Points
                .Select(p => new Coordinate(p.X, p.Y))
                .ToArray();

            if (coords.Length < 2)
            {
                return null;
            }

            return GeometryFactory.CreateLineString(coords);
        }

        return null;
    }

    private static Polygon? ConvertPolygon(Polygon2D polygon)
    {
        Coordinate[] exterior = polygon.ExteriorRing
            .Select(p => new Coordinate(p.X, p.Y))
            .ToArray();

        if (exterior.Length < 4)
        {
            return null;
        }

        LinearRing shell = GeometryFactory.CreateLinearRing(exterior);
        LinearRing[] holes = polygon.InteriorRings
            .Select(ring => GeometryFactory.CreateLinearRing(
                ring.Select(p => new Coordinate(p.X, p.Y)).ToArray()))
            .ToArray();

        return GeometryFactory.CreatePolygon(shell, holes);
    }

    private static object? CoerceAttributeValue(object? value, ExportAttributeType type)
    {
        if (value == null)
        {
            return type switch
            {
                ExportAttributeType.Integer => 0,
                ExportAttributeType.Real => 0.0,
                ExportAttributeType.Boolean => false,
                _ => string.Empty,
            };
        }

        if (type == ExportAttributeType.Text && value is string text && text.Length > 254)
        {
            return text.Substring(0, 254);
        }

        return value;
    }

    private static Dictionary<string, string> BuildColumnNameMap(IReadOnlyList<AttributeDefinition> attributes)
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        HashSet<string> usedNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (AttributeDefinition attr in attributes)
        {
            string shortened = attr.Name.Length <= 10
                ? attr.Name
                : TruncateColumnName(attr.Name);

            string candidate = shortened;
            int suffix = 1;
            while (!usedNames.Add(candidate))
            {
                string suffixStr = suffix.ToString();
                candidate = shortened.Substring(0, Math.Min(shortened.Length, 10 - suffixStr.Length)) + suffixStr;
                suffix++;
            }

            map[attr.Name] = candidate;
        }

        return map;
    }

    private static string TruncateColumnName(string name)
    {
        // Remove underscores and vowels from the middle to shorten
        string noUnderscores = name.Replace("_", string.Empty);
        if (noUnderscores.Length <= 10)
        {
            return noUnderscores;
        }

        // Keep first 5 and last 5 characters
        return noUnderscores.Substring(0, 5) + noUnderscores.Substring(noUnderscores.Length - 5);
    }

    private static void WritePrjFile(string shapefilePath, int srsId)
    {
        if (CoordinateSystemCatalog.TryGetDefinitionWkt(srsId, out string wkt) && wkt.Length > 0)
        {
            string prjPath = Path.ChangeExtension(shapefilePath, ".prj");
            File.WriteAllText(prjPath, wkt);
        }
    }

    private static void WriteCpgFile(string shapefilePath)
    {
        string cpgPath = Path.ChangeExtension(shapefilePath, ".cpg");
        File.WriteAllText(cpgPath, "UTF-8", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string NormalizeShapefilePath(string shapefilePath)
    {
        return shapefilePath.EndsWith(".shp", StringComparison.OrdinalIgnoreCase)
            ? shapefilePath
            : shapefilePath + ".shp";
    }

    private static string BuildLayerPath(string shapefilePath, string layerName)
    {
        string directory = Path.GetDirectoryName(shapefilePath) ?? string.Empty;
        string fileName = Path.GetFileNameWithoutExtension(shapefilePath);
        return Path.Combine(directory, $"{fileName}_{layerName}.shp");
    }
}
