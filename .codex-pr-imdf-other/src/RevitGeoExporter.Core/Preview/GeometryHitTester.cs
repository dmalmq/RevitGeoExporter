using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Preview;

public static class GeometryHitTester
{
    public static int FindHitIndex<TItem>(
        IReadOnlyList<TItem> items,
        Func<TItem, IExportFeature> featureSelector,
        Point2D worldPoint,
        double toleranceWorld)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (featureSelector is null)
        {
            throw new ArgumentNullException(nameof(featureSelector));
        }

        for (int i = items.Count - 1; i >= 0; i--)
        {
            IExportFeature feature = featureSelector(items[i]);
            if (feature != null && IsHit(feature, worldPoint, toleranceWorld))
            {
                return i;
            }
        }

        return -1;
    }

    public static bool IsHit(IExportFeature feature, Point2D worldPoint, double toleranceWorld)
    {
        if (feature is null)
        {
            throw new ArgumentNullException(nameof(feature));
        }

        double tolerance = Math.Max(0d, toleranceWorld);
        switch (feature)
        {
            case ExportPolygon polygon:
                return IsPolygonHit(polygon, worldPoint, tolerance);
            case ExportLineString lineString:
                return GetDistanceToLineString(lineString.LineString, worldPoint) <= tolerance;
            default:
                return false;
        }
    }

    private static bool IsPolygonHit(ExportPolygon polygon, Point2D worldPoint, double toleranceWorld)
    {
        for (int i = 0; i < polygon.Polygons.Count; i++)
        {
            Polygon2D candidate = polygon.Polygons[i];
            if (Contains(candidate, worldPoint))
            {
                return true;
            }

            if (GetDistanceToRing(candidate.ExteriorRing, worldPoint) <= toleranceWorld)
            {
                return true;
            }

            for (int j = 0; j < candidate.InteriorRings.Count; j++)
            {
                if (GetDistanceToRing(candidate.InteriorRings[j], worldPoint) <= toleranceWorld)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Contains(Polygon2D polygon, Point2D point)
    {
        if (!IsPointInRing(polygon.ExteriorRing, point))
        {
            return false;
        }

        for (int i = 0; i < polygon.InteriorRings.Count; i++)
        {
            if (IsPointInRing(polygon.InteriorRings[i], point))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPointInRing(IReadOnlyList<Point2D> ring, Point2D point)
    {
        bool inside = false;
        int count = ring.Count;
        if (count < 4)
        {
            return false;
        }

        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            Point2D current = ring[i];
            Point2D previous = ring[j];
            bool intersects = ((current.Y > point.Y) != (previous.Y > point.Y)) &&
                              (point.X < (((previous.X - current.X) * (point.Y - current.Y)) /
                                          ((previous.Y - current.Y) == 0d ? double.Epsilon : (previous.Y - current.Y))) + current.X);
            if (intersects)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double GetDistanceToLineString(LineString2D lineString, Point2D point)
    {
        double best = double.MaxValue;
        for (int i = 0; i < lineString.Points.Count - 1; i++)
        {
            double distance = GetDistanceToSegment(point, lineString.Points[i], lineString.Points[i + 1]);
            if (distance < best)
            {
                best = distance;
            }
        }

        return best;
    }

    private static double GetDistanceToRing(IReadOnlyList<Point2D> ring, Point2D point)
    {
        double best = double.MaxValue;
        for (int i = 0; i < ring.Count - 1; i++)
        {
            double distance = GetDistanceToSegment(point, ring[i], ring[i + 1]);
            if (distance < best)
            {
                best = distance;
            }
        }

        return best;
    }

    private static double GetDistanceToSegment(Point2D point, Point2D start, Point2D end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= double.Epsilon)
        {
            return Distance(point, start);
        }

        double t = (((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / lengthSquared;
        t = Math.Max(0d, Math.Min(1d, t));
        Point2D projection = new(start.X + (dx * t), start.Y + (dy * t));
        return Distance(point, projection);
    }

    private static double Distance(Point2D left, Point2D right)
    {
        double dx = right.X - left.X;
        double dy = right.Y - left.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
