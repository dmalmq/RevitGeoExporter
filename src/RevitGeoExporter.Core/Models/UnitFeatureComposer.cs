using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitGeoExporter.Core.Models;

public static class UnitFeatureComposer
{
    private static readonly HashSet<string> PreservedGeometryAttributeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "level_id",
        "source",
        "display_point",
        "source_element_id",
        "source_document_key",
        "source_document_name",
        "has_persisted_export_id",
        "is_linked_source",
        "source_link_instance_id",
        "source_link_instance_name",
    };

    public static IReadOnlyList<ExportPolygon> Compose(
        IReadOnlyList<ExportPolygon> floorFeatures,
        IReadOnlyList<ExportPolygon> roomFeatures,
        UnitGeometrySource geometrySource,
        UnitAttributeSource attributeSource)
    {
        IReadOnlyList<ExportPolygon> geometryFeatures = geometrySource == UnitGeometrySource.Rooms
            ? roomFeatures ?? Array.Empty<ExportPolygon>()
            : floorFeatures ?? Array.Empty<ExportPolygon>();
        IReadOnlyList<ExportPolygon> floorPool = floorFeatures ?? Array.Empty<ExportPolygon>();
        IReadOnlyList<ExportPolygon> roomPool = roomFeatures ?? Array.Empty<ExportPolygon>();

        if (geometryFeatures.Count == 0)
        {
            return Array.Empty<ExportPolygon>();
        }

        List<ExportPolygon> composed = new(geometryFeatures.Count);
        for (int i = 0; i < geometryFeatures.Count; i++)
        {
            ExportPolygon geometryFeature = geometryFeatures[i];
            ExportPolygon? attributeFeature = ResolveAttributeFeature(
                geometryFeature,
                floorPool,
                roomPool,
                geometrySource,
                attributeSource);
            string attributeSourceKind = ResolveAttributeSourceKind(
                geometrySource,
                attributeSource,
                attributeFeature,
                geometryFeature);
            composed.Add(ComposeFeature(geometryFeature, attributeFeature, geometrySource, attributeSourceKind));
        }

        return composed;
    }

    private static ExportPolygon? ResolveAttributeFeature(
        ExportPolygon geometryFeature,
        IReadOnlyList<ExportPolygon> floorPool,
        IReadOnlyList<ExportPolygon> roomPool,
        UnitGeometrySource geometrySource,
        UnitAttributeSource attributeSource)
    {
        if (geometryFeature == null)
        {
            return null;
        }

        if (attributeSource == UnitAttributeSource.Unset)
        {
            attributeSource = geometrySource == UnitGeometrySource.Rooms
                ? UnitAttributeSource.Rooms
                : UnitAttributeSource.Hybrid;
        }

        return geometrySource switch
        {
            UnitGeometrySource.Rooms => attributeSource == UnitAttributeSource.Floors
                ? FindBestSpatialMatch(geometryFeature, floorPool)
                : geometryFeature,
            _ => attributeSource == UnitAttributeSource.Floors
                ? geometryFeature
                : FindBestSpatialMatch(geometryFeature, roomPool),
        };
    }

    private static string ResolveAttributeSourceKind(
        UnitGeometrySource geometrySource,
        UnitAttributeSource attributeSource,
        ExportPolygon? attributeFeature,
        ExportPolygon geometryFeature)
    {
        if (attributeFeature == null || ReferenceEquals(attributeFeature, geometryFeature))
        {
            return geometrySource == UnitGeometrySource.Rooms ? "room" : "floor";
        }

        return attributeSource == UnitAttributeSource.Floors ? "floor" : "room";
    }

    private static ExportPolygon ComposeFeature(
        ExportPolygon geometryFeature,
        ExportPolygon? attributeFeature,
        UnitGeometrySource geometrySource,
        string attributeSourceKind)
    {
        Dictionary<string, object?> attributes = geometryFeature.Attributes.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        if (attributeFeature != null)
        {
            foreach (KeyValuePair<string, object?> entry in attributeFeature.Attributes)
            {
                if (PreservedGeometryAttributeKeys.Contains(entry.Key))
                {
                    continue;
                }

                attributes[entry.Key] = entry.Value;
            }
        }

        attributes["unit_geometry_source_kind"] = geometrySource == UnitGeometrySource.Rooms ? "room" : "floor";
        attributes["unit_attribute_source_kind"] = attributeSourceKind;
        return new ExportPolygon(ClonePolygons(geometryFeature.Polygons), attributes);
    }

    private static IReadOnlyList<Polygon2D> ClonePolygons(IReadOnlyList<Polygon2D> polygons)
    {
        return polygons
            .Select(ClonePolygon)
            .ToList();
    }

    private static Polygon2D ClonePolygon(Polygon2D polygon)
    {
        IReadOnlyList<Point2D> exterior = polygon.ExteriorRing
            .Select(point => new Point2D(point.X, point.Y))
            .ToList();
        IReadOnlyList<IReadOnlyList<Point2D>> interior = polygon.InteriorRings
            .Select(ring => (IReadOnlyList<Point2D>)ring.Select(point => new Point2D(point.X, point.Y)).ToList())
            .ToList();
        return new Polygon2D(exterior, interior);
    }

    private static ExportPolygon? FindBestSpatialMatch(ExportPolygon geometryFeature, IReadOnlyList<ExportPolygon> candidates)
    {
        if (geometryFeature == null || candidates == null || candidates.Count == 0)
        {
            return null;
        }

        FeatureSpatialInfo geometryInfo = BuildSpatialInfo(geometryFeature);
        ExportPolygon? best = null;
        double bestScore = double.MinValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            ExportPolygon candidate = candidates[i];
            FeatureSpatialInfo candidateInfo = BuildSpatialInfo(candidate);
            if (!CanMatch(geometryInfo, candidateInfo))
            {
                continue;
            }

            double score = ScoreMatch(geometryInfo, candidateInfo);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private static bool CanMatch(FeatureSpatialInfo left, FeatureSpatialInfo right)
    {
        return ContainsPoint(left, right.PrimaryCentroid) ||
               ContainsPoint(right, left.PrimaryCentroid) ||
               BoundingBoxesOverlap(left, right);
    }

    private static double ScoreMatch(FeatureSpatialInfo left, FeatureSpatialInfo right)
    {
        double leftArea = Math.Max(left.TotalArea, 1e-9d);
        double rightArea = Math.Max(right.TotalArea, 1e-9d);
        double areaRatio = Math.Min(leftArea, rightArea) / Math.Max(leftArea, rightArea);
        double distance = Distance(left.PrimaryCentroid, right.PrimaryCentroid);
        double containmentScore = ContainsPoint(left, right.PrimaryCentroid) || ContainsPoint(right, left.PrimaryCentroid)
            ? 1d
            : 0.5d;
        return (containmentScore * 100d) + (areaRatio * 10d) - distance;
    }

    private static double Distance(Point2D left, Point2D right)
    {
        double dx = left.X - right.X;
        double dy = left.Y - right.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static bool BoundingBoxesOverlap(FeatureSpatialInfo left, FeatureSpatialInfo right)
    {
        return left.MinX <= right.MaxX &&
               left.MaxX >= right.MinX &&
               left.MinY <= right.MaxY &&
               left.MaxY >= right.MinY;
    }

    private static bool ContainsPoint(FeatureSpatialInfo info, Point2D point)
    {
        return info.Polygons.Any(polygon => PolygonContainsPoint(polygon, point));
    }

    private static bool PolygonContainsPoint(Polygon2D polygon, Point2D point)
    {
        if (!RingContainsPoint(polygon.ExteriorRing, point))
        {
            return false;
        }

        return !polygon.InteriorRings.Any(ring => RingContainsPoint(ring, point));
    }

    private static bool RingContainsPoint(IReadOnlyList<Point2D> ring, Point2D point)
    {
        bool inside = false;
        int last = ring.Count - 1;
        for (int i = 0, j = last; i < ring.Count; j = i++)
        {
            Point2D left = ring[i];
            Point2D right = ring[j];
            bool intersects = ((left.Y > point.Y) != (right.Y > point.Y)) &&
                              (point.X < ((right.X - left.X) * (point.Y - left.Y) / ((right.Y - left.Y) + double.Epsilon)) + left.X);
            if (intersects)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static FeatureSpatialInfo BuildSpatialInfo(ExportPolygon feature)
    {
        List<Polygon2D> polygons = feature.Polygons.ToList();
        Polygon2D primaryPolygon = polygons
            .OrderByDescending(ComputeArea)
            .First();
        double minX = polygons.SelectMany(polygon => polygon.ExteriorRing).Min(point => point.X);
        double minY = polygons.SelectMany(polygon => polygon.ExteriorRing).Min(point => point.Y);
        double maxX = polygons.SelectMany(polygon => polygon.ExteriorRing).Max(point => point.X);
        double maxY = polygons.SelectMany(polygon => polygon.ExteriorRing).Max(point => point.Y);

        return new FeatureSpatialInfo(
            polygons,
            DisplayPointCalculator.CalculateCentroid(primaryPolygon),
            polygons.Sum(ComputeArea),
            minX,
            minY,
            maxX,
            maxY);
    }

    private static double ComputeArea(Polygon2D polygon)
    {
        double exterior = Math.Abs(ComputeSignedArea(polygon.ExteriorRing));
        double interior = polygon.InteriorRings.Sum(ring => Math.Abs(ComputeSignedArea(ring)));
        return Math.Max(0d, exterior - interior);
    }

    private static double ComputeSignedArea(IReadOnlyList<Point2D> ring)
    {
        if (ring == null || ring.Count < 4)
        {
            return 0d;
        }

        double area = 0d;
        for (int i = 0; i < ring.Count - 1; i++)
        {
            area += (ring[i].X * ring[i + 1].Y) - (ring[i + 1].X * ring[i].Y);
        }

        return area / 2d;
    }

    private sealed class FeatureSpatialInfo
    {
        public FeatureSpatialInfo(
            IReadOnlyList<Polygon2D> polygons,
            Point2D primaryCentroid,
            double totalArea,
            double minX,
            double minY,
            double maxX,
            double maxY)
        {
            Polygons = polygons;
            PrimaryCentroid = primaryCentroid;
            TotalArea = totalArea;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public IReadOnlyList<Polygon2D> Polygons { get; }

        public Point2D PrimaryCentroid { get; }

        public double TotalArea { get; }

        public double MinX { get; }

        public double MinY { get; }

        public double MaxX { get; }

        public double MaxY { get; }
    }
}
