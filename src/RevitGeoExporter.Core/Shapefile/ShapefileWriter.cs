using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
    private static readonly Encoding DbfEncoding = Encoding.UTF8;

    public void Write(string shapefilePath, int srsId, IReadOnlyCollection<ExportLayer> layers, CancellationToken cancellationToken = default)
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
        List<ShapefileLayerPlan> plans = BuildLayerPlans(normalizedShapefilePath, layers);
        if (plans.Count == 0)
        {
            return;
        }

        string outputDirectory = Path.GetDirectoryName(normalizedShapefilePath) ?? string.Empty;
        string tempDirectory = Path.Combine(
            string.IsNullOrWhiteSpace(outputDirectory) ? Environment.CurrentDirectory : outputDirectory,
            $".{Path.GetFileNameWithoutExtension(normalizedShapefilePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            foreach (ShapefileLayerPlan plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string tempLayerPath = Path.Combine(tempDirectory, Path.GetFileName(plan.OutputPath));
                if (WriteLayer(tempLayerPath, srsId, plan.Layer, cancellationToken))
                {
                    ReplaceShapefileSet(tempLayerPath, plan.OutputPath);
                }
            }
        }
        finally
        {
            DeleteDirectoryIfExists(tempDirectory);
        }
    }

    private static bool WriteLayer(string shapefilePath, int srsId, ExportLayer layer, CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();
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
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        DbaseFieldDescriptor[] fields = BuildFields(layer.Attributes, columnNameMap);

        // NTS IO ShapeFile 2.0.0: AddColumn sets header.Encoding = DefaultEncoding (CP1252) when
        // encoding is null. DbaseFileWriter.Write then sees CP1252 != UTF8 and tries to override
        // it, which throws the "only allowed once" exception. Pre-setting encoding in the header
        // constructor prevents AddColumn from touching it, and the writer's equality guard
        // (!object.Equals(header.Encoding, _encoding)) then short-circuits since both are UTF8.
        DbaseFileHeader header = new DbaseFileHeader(DbfEncoding);
        header.NumRecords = features.Count;
        foreach (DbaseFieldDescriptor field in fields)
            header.AddColumn(field.Name, field.DbaseType, field.Length, field.DecimalCount);

        ShapefileDataWriter sdw = new ShapefileDataWriter(shapefilePath, GeometryFactory, DbfEncoding);
        sdw.Header = header;
        sdw.Write(features);

        cancellationToken.ThrowIfCancellationRequested();

        WritePrjFile(shapefilePath, srsId);
        WriteCpgFile(shapefilePath);
        return true;
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

    private static DbaseFieldDescriptor[] BuildFields(
        IReadOnlyList<AttributeDefinition> attributes,
        IReadOnlyDictionary<string, string> columnNameMap)
    {
        DbaseFieldDescriptor[] fields = new DbaseFieldDescriptor[attributes.Count];
        for (int i = 0; i < attributes.Count; i++)
        {
            AttributeDefinition attrDef = attributes[i];
            string columnName = columnNameMap[attrDef.Name];
            DbaseFieldDescriptor field = new()
            {
                Name = columnName,
            };

            switch (attrDef.Type)
            {
                case ExportAttributeType.Integer:
                    field.DbaseType = 'N';
                    field.Length = 18;
                    field.DecimalCount = 0;
                    break;
                case ExportAttributeType.Real:
                    field.DbaseType = 'N';
                    field.Length = 19;
                    field.DecimalCount = 8;
                    break;
                case ExportAttributeType.Boolean:
                    field.DbaseType = 'L';
                    field.Length = 1;
                    field.DecimalCount = 0;
                    break;
                case ExportAttributeType.Text:
                    field.DbaseType = 'C';
                    field.Length = 254;
                    field.DecimalCount = 0;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(attributes), attrDef.Type, "Unsupported shapefile attribute type.");
            }

            fields[i] = field;
        }

        return fields;
    }

    private static ShapeGeometryType GetShapeGeometryType(GpkgGeometryType geometryType)
    {
        switch (geometryType)
        {
            case GpkgGeometryType.LineString:
                return ShapeGeometryType.LineString;
            case GpkgGeometryType.Polygon:
            case GpkgGeometryType.MultiPolygon:
                return ShapeGeometryType.Polygon;
            default:
                throw new ArgumentOutOfRangeException(nameof(geometryType), geometryType, "Unsupported shapefile geometry type.");
        }
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

    private static List<ShapefileLayerPlan> BuildLayerPlans(string normalizedShapefilePath, IReadOnlyCollection<ExportLayer> layers)
    {
        List<ExportLayer> writableLayers = layers
            .Where(layer => layer.Features.Count > 0)
            .ToList();
        List<ShapefileLayerPlan> plans = new(writableLayers.Count);
        foreach (ExportLayer layer in writableLayers)
        {
            string layerPath = writableLayers.Count == 1
                ? normalizedShapefilePath
                : BuildLayerPath(normalizedShapefilePath, layer.Name);
            plans.Add(new ShapefileLayerPlan(layer, layerPath));
        }

        return plans;
    }

    private static void ReplaceShapefileSet(string sourceShapefilePath, string destinationShapefilePath)
    {
        string sourceDirectory = Path.GetDirectoryName(sourceShapefilePath) ?? string.Empty;
        string destinationDirectory = Path.GetDirectoryName(destinationShapefilePath) ?? string.Empty;
        string sourceStem = Path.GetFileNameWithoutExtension(sourceShapefilePath);
        string destinationStem = Path.GetFileNameWithoutExtension(destinationShapefilePath);

        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new FileNotFoundException("Could not resolve shapefile source or destination directory.", sourceShapefilePath);
        }

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            destinationDirectory = Environment.CurrentDirectory;
        }

        string[] sourceFiles = Directory.GetFiles(sourceDirectory, sourceStem + ".*");
        if (sourceFiles.Length == 0)
        {
            throw new FileNotFoundException("Could not find any shapefile components for the exported artifact.", sourceShapefilePath);
        }

        Directory.CreateDirectory(destinationDirectory);
        string backupDirectory = Path.Combine(destinationDirectory, $".{destinationStem}.{Guid.NewGuid():N}.bak");
        List<string> destinationFiles = sourceFiles
            .Select(sourceFile => Path.Combine(destinationDirectory, destinationStem + Path.GetExtension(sourceFile)))
            .ToList();
        Dictionary<string, string> backupsByDestination = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (string destinationFile in destinationFiles.Where(File.Exists))
            {
                Directory.CreateDirectory(backupDirectory);
                string backupPath = Path.Combine(backupDirectory, Path.GetFileName(destinationFile));
                File.Copy(destinationFile, backupPath, overwrite: true);
                backupsByDestination[destinationFile] = backupPath;
            }

            for (int i = 0; i < sourceFiles.Length; i++)
            {
                MoveWithReplace(sourceFiles[i], destinationFiles[i]);
            }
        }
        catch
        {
            RestoreBackups(destinationFiles, backupsByDestination);
            throw;
        }
        finally
        {
            DeleteDirectoryIfExists(backupDirectory);
        }
    }

    private static void MoveWithReplace(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Replace(sourcePath, destinationPath, null);
            return;
        }

        File.Move(sourcePath, destinationPath);
    }

    private static void RestoreBackups(
        IEnumerable<string> destinationFiles,
        IReadOnlyDictionary<string, string> backupsByDestination)
    {
        foreach (string destinationFile in destinationFiles)
        {
            if (backupsByDestination.TryGetValue(destinationFile, out string? backupPath) && File.Exists(backupPath))
            {
                File.Copy(backupPath, destinationFile, overwrite: true);
            }
            else if (File.Exists(destinationFile))
            {
                File.Delete(destinationFile);
            }
        }
    }

    private static void DeleteDirectoryIfExists(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ShapefileLayerPlan
    {
        public ShapefileLayerPlan(ExportLayer layer, string outputPath)
        {
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
            OutputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        }

        public ExportLayer Layer { get; }

        public string OutputPath { get; }
    }
}
