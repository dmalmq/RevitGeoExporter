using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Precision;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Schema;
using RevitGeoExporter.Export;

namespace RevitGeoExporter.Extractors;

public sealed class LevelBoundaryBuilder
{
    private const double FeetToMeters = 0.3048d;
    private static readonly GeometryFactory GeometryFactory = new();

    public bool TryBuild(
        Level level,
        string levelId,
        int ordinal,
        IReadOnlyList<ExportPolygon> unitFeatures,
        GeometryRepairOptions geometryRepairOptions,
        string sourceDocumentKey,
        string sourceDocumentName,
        bool hasPersistedExportId,
        SchemaProfile? schemaProfile,
        string? viewName,
        ICollection<string> warnings,
        out ExportPolygon? boundaryFeature)
    {
        boundaryFeature = null;
        if (level is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(levelId))
        {
            return false;
        }

        if (unitFeatures is null || unitFeatures.Count == 0)
        {
            return false;
        }

        List<Geometry> unitGeometries = BuildUnitGeometries(unitFeatures);
        if (unitGeometries.Count == 0)
        {
            return false;
        }

        Geometry unioned = UnaryUnionOp.Union(unitGeometries);
        if (unioned.IsEmpty)
        {
            return false;
        }

        Geometry repaired = HealLevelGeometry(unioned, geometryRepairOptions, viewName, warnings);
        List<Polygon2D> polygons = ExtractPolygons(repaired);
        if (polygons.Count == 0)
        {
            return false;
        }

        boundaryFeature = new ExportPolygon(
            polygons,
            BuildAttributes(
                level,
                levelId,
                ordinal,
                sourceDocumentKey,
                sourceDocumentName,
                hasPersistedExportId,
                schemaProfile,
                viewName,
                warnings));

        return true;
    }

    private static Geometry HealLevelGeometry(
        Geometry unioned,
        GeometryRepairOptions geometryRepairOptions,
        string? viewName,
        ICollection<string> warnings)
    {
        Geometry normalized = unioned.Buffer(0d);
        double threshold = geometryRepairOptions?.MergeNearbyBoundaryThresholdMeters ?? 0d;
        if (threshold <= 0d)
        {
            return normalized;
        }

        try
        {
            Geometry healed = ApplyCloseOpen(normalized, threshold);
            if (!healed.IsEmpty)
            {
                return healed;
            }

            warnings.Add($"Level boundary topology healing produced empty geometry for view '{viewName ?? "(unknown)"}'; using normalized union geometry.");
            return normalized;
        }
        catch (TopologyException)
        {
            try
            {
                GeometryPrecisionReducer reducer = new(new PrecisionModel(100_000d));
                Geometry reduced = reducer.Reduce(normalized);
                Geometry healedReduced = ApplyCloseOpen(reduced, threshold);
                warnings.Add($"Level boundary topology healing required reduced precision for view '{viewName ?? "(unknown)"}'.");
                return healedReduced.IsEmpty ? normalized : healedReduced;
            }
            catch (TopologyException)
            {
                warnings.Add($"Level boundary topology healing failed for view '{viewName ?? "(unknown)"}'; using normalized union geometry.");
                return normalized;
            }
        }
    }

    private static Geometry ApplyCloseOpen(Geometry geometry, double threshold)
    {
        Geometry closed = geometry.Buffer(threshold).Buffer(-threshold);
        Geometry opened = closed.Buffer(-threshold).Buffer(threshold);
        return opened.Buffer(0d);
    }

    private static Dictionary<string, object?> BuildAttributes(
        Level level,
        string levelId,
        int ordinal,
        string sourceDocumentKey,
        string sourceDocumentName,
        bool hasPersistedExportId,
        SchemaProfile? schemaProfile,
        string? viewName,
        ICollection<string> warnings)
    {
        Dictionary<string, object?> attributes = new()
        {
            ["id"] = levelId,
            ["level_name"] = level.Name,
            ["ordinal"] = ordinal,
            ["elevation_m"] = level.Elevation * FeetToMeters,
            ["source_document_key"] = sourceDocumentKey,
            ["source_document_name"] = sourceDocumentName,
            ["has_persisted_export_id"] = hasPersistedExportId,
            ["is_linked_source"] = false,
            ["source_link_instance_id"] = null,
            ["source_link_instance_name"] = null,
        };
        SchemaAttributeMapper.ApplyMappings(
            schemaProfile,
            SchemaLayerType.Level,
            attributes,
            level,
            viewName,
            warnings);
        return attributes;
    }

    private static List<Geometry> BuildUnitGeometries(IReadOnlyList<ExportPolygon> unitFeatures)
    {
        List<Geometry> geometries = new();
        foreach (ExportPolygon feature in unitFeatures)
        {
            foreach (Polygon2D polygon in feature.Polygons)
            {
                Geometry? ntsGeometry = ToNtsGeometry(polygon);
                if (ntsGeometry == null || ntsGeometry.IsEmpty)
                {
                    continue;
                }

                AddPolygonGeometryParts(geometries, ntsGeometry);
            }
        }

        return geometries;
    }

    private static Geometry? ToNtsGeometry(Polygon2D polygon)
    {
        if (!TryCreateLinearRing(polygon.ExteriorRing, out LinearRing? shell))
        {
            return null;
        }

        List<LinearRing> holes = new();
        for (int i = 0; i < polygon.InteriorRings.Count; i++)
        {
            if (TryCreateLinearRing(polygon.InteriorRings[i], out LinearRing? hole) && hole != null)
            {
                holes.Add(hole);
            }
        }

        Polygon created = GeometryFactory.CreatePolygon(shell, holes.ToArray());
        if (!created.IsValid)
        {
            Geometry healed = created.Buffer(0d);
            return healed;
        }

        return created;
    }

    private static void AddPolygonGeometryParts(ICollection<Geometry> target, Geometry geometry)
    {
        if (geometry == null || geometry.IsEmpty)
        {
            return;
        }

        switch (geometry)
        {
            case Polygon polygon:
                target.Add(polygon);
                break;
            case MultiPolygon multiPolygon:
                for (int i = 0; i < multiPolygon.NumGeometries; i++)
                {
                    AddPolygonGeometryParts(target, multiPolygon.GetGeometryN(i));
                }

                break;
            case GeometryCollection collection:
                for (int i = 0; i < collection.NumGeometries; i++)
                {
                    AddPolygonGeometryParts(target, collection.GetGeometryN(i));
                }

                break;
        }
    }

    private static bool TryCreateLinearRing(IReadOnlyList<Point2D> ringPoints, out LinearRing? ring)
    {
        ring = null;
        if (ringPoints == null || ringPoints.Count < 4)
        {
            return false;
        }

        List<Coordinate> coords = new(ringPoints.Count + 1);
        for (int i = 0; i < ringPoints.Count; i++)
        {
            coords.Add(new Coordinate(ringPoints[i].X, ringPoints[i].Y));
        }

        Coordinate first = coords[0];
        Coordinate last = coords[coords.Count - 1];
        if (!first.Equals2D(last))
        {
            coords.Add(new Coordinate(first.X, first.Y));
        }

        if (coords.Count < 4)
        {
            return false;
        }

        ring = GeometryFactory.CreateLinearRing(coords.ToArray());
        return !ring.IsEmpty;
    }

    private static List<Polygon2D> ExtractPolygons(Geometry geometry)
    {
        List<Polygon2D> polygons = new();
        switch (geometry)
        {
            case Polygon polygon:
                Polygon2D? converted = ToPolygon2D(polygon);
                if (converted != null)
                {
                    polygons.Add(converted);
                }

                break;
            case MultiPolygon multiPolygon:
                for (int i = 0; i < multiPolygon.NumGeometries; i++)
                {
                    if (multiPolygon.GetGeometryN(i) is Polygon child)
                    {
                        Polygon2D? childPolygon = ToPolygon2D(child);
                        if (childPolygon != null)
                        {
                            polygons.Add(childPolygon);
                        }
                    }
                }

                break;
            case GeometryCollection collection:
                for (int i = 0; i < collection.NumGeometries; i++)
                {
                    polygons.AddRange(ExtractPolygons(collection.GetGeometryN(i)));
                }

                break;
        }

        return polygons;
    }

    private static Polygon2D? ToPolygon2D(Polygon polygon)
    {
        IReadOnlyList<Point2D>? exterior = ToPointList(polygon.ExteriorRing.Coordinates);
        if (exterior == null)
        {
            return null;
        }

        List<IReadOnlyList<Point2D>> interior = new();
        for (int i = 0; i < polygon.NumInteriorRings; i++)
        {
            IReadOnlyList<Point2D>? ring = ToPointList(polygon.GetInteriorRingN(i).Coordinates);
            if (ring != null)
            {
                interior.Add(ring);
            }
        }

        return new Polygon2D(exterior, interior);
    }

    private static IReadOnlyList<Point2D>? ToPointList(Coordinate[] coordinates)
    {
        if (coordinates == null || coordinates.Length < 4)
        {
            return null;
        }

        List<Point2D> points = new(coordinates.Length);
        for (int i = 0; i < coordinates.Length; i++)
        {
            points.Add(new Point2D(coordinates[i].X, coordinates[i].Y));
        }

        return points;
    }
}
