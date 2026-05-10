using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Geometry;

public readonly struct ClipRegion2D
{
    private const double Epsilon = 1e-9d;
    private readonly IReadOnlyList<Point2D>? _vertices;

    private ClipRegion2D(IReadOnlyList<Point2D> vertices)
    {
        _vertices = vertices;

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;
        foreach (Point2D vertex in vertices)
        {
            if (vertex.X < minX) minX = vertex.X;
            if (vertex.Y < minY) minY = vertex.Y;
            if (vertex.X > maxX) maxX = vertex.X;
            if (vertex.Y > maxY) maxY = vertex.Y;
        }

        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public IReadOnlyList<Point2D> Vertices => _vertices ?? Array.Empty<Point2D>();
    public bool IsEmpty => _vertices == null || _vertices.Count < 3;
    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }

    public static bool TryCreate(IReadOnlyList<Point2D>? vertices, out ClipRegion2D region)
    {
        region = default;
        if (vertices == null || vertices.Count < 3)
        {
            return false;
        }

        List<Point2D> normalized = new(vertices.Count);
        foreach (Point2D vertex in vertices)
        {
            if (normalized.Count == 0 || !SamePoint(normalized[normalized.Count - 1], vertex))
            {
                normalized.Add(vertex);
            }
        }

        if (normalized.Count > 1 && SamePoint(normalized[0], normalized[normalized.Count - 1]))
        {
            normalized.RemoveAt(normalized.Count - 1);
        }

        if (normalized.Count < 3 || Math.Abs(SignedArea(normalized)) <= Epsilon)
        {
            return false;
        }

        region = new ClipRegion2D(normalized);
        return true;
    }

    public static ClipRegion2D FromAxisAlignedBounds(double minX, double minY, double maxX, double maxY)
    {
        if (!TryCreate(
                new[]
                {
                    new Point2D(minX, minY),
                    new Point2D(maxX, minY),
                    new Point2D(maxX, maxY),
                    new Point2D(minX, maxY),
                },
                out ClipRegion2D region))
        {
            return default;
        }

        return region;
    }

    public static ClipRegion2D FromFootprint(ScopeBoxFootprint footprint)
    {
        Point2D ToWorld(double localX, double localY)
        {
            return new Point2D(
                footprint.Origin.X + (footprint.XBasis.X * localX) + (footprint.YBasis.X * localY),
                footprint.Origin.Y + (footprint.XBasis.Y * localX) + (footprint.YBasis.Y * localY));
        }

        if (!TryCreate(
                new[]
                {
                    ToWorld(footprint.MinX, footprint.MinY),
                    ToWorld(footprint.MaxX, footprint.MinY),
                    ToWorld(footprint.MaxX, footprint.MaxY),
                    ToWorld(footprint.MinX, footprint.MaxY),
                },
                out ClipRegion2D region))
        {
            return default;
        }

        return region;
    }

    public bool ContainsPoint(Point2D point)
    {
        return !IsEmpty && ContainsPoint(Vertices, point);
    }

    public bool IntersectsBounds(double minX, double minY, double maxX, double maxY)
    {
        return IntersectsPolygon(new[]
        {
            new Point2D(minX, minY),
            new Point2D(maxX, minY),
            new Point2D(maxX, maxY),
            new Point2D(minX, maxY),
        });
    }

    public bool IntersectsPolygon(IReadOnlyList<Point2D>? polygon)
    {
        if (IsEmpty || polygon == null || polygon.Count < 3)
        {
            return false;
        }

        if (!TryGetBounds(polygon, out double minX, out double minY, out double maxX, out double maxY) ||
            maxX < MinX || minX > MaxX || maxY < MinY || minY > MaxY)
        {
            return false;
        }

        IReadOnlyList<Point2D> vertices = Vertices;
        foreach (Point2D point in polygon)
        {
            if (ContainsPoint(vertices, point))
            {
                return true;
            }
        }

        foreach (Point2D point in vertices)
        {
            if (ContainsPoint(polygon, point))
            {
                return true;
            }
        }

        for (int i = 0; i < vertices.Count; i++)
        {
            Point2D a = vertices[i];
            Point2D b = vertices[(i + 1) % vertices.Count];
            for (int j = 0; j < polygon.Count; j++)
            {
                Point2D c = polygon[j];
                Point2D d = polygon[(j + 1) % polygon.Count];
                if (SegmentsIntersect(a, b, c, d))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetBounds(
        IReadOnlyList<Point2D> points,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        minX = double.MaxValue;
        minY = double.MaxValue;
        maxX = double.MinValue;
        maxY = double.MinValue;
        if (points.Count == 0)
        {
            return false;
        }

        foreach (Point2D point in points)
        {
            if (point.X < minX) minX = point.X;
            if (point.Y < minY) minY = point.Y;
            if (point.X > maxX) maxX = point.X;
            if (point.Y > maxY) maxY = point.Y;
        }

        return true;
    }

    private static bool ContainsPoint(IReadOnlyList<Point2D> polygon, Point2D point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Point2D current = polygon[i];
            Point2D previous = polygon[j];
            if (IsPointOnSegment(previous, current, point))
            {
                return true;
            }

            bool crosses = (current.Y > point.Y) != (previous.Y > point.Y);
            if (crosses)
            {
                double xAtY = ((previous.X - current.X) * (point.Y - current.Y) /
                    (previous.Y - current.Y)) + current.X;
                if (point.X <= xAtY + Epsilon)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    private static bool SegmentsIntersect(Point2D a, Point2D b, Point2D c, Point2D d)
    {
        double abC = Cross(a, b, c);
        double abD = Cross(a, b, d);
        double cdA = Cross(c, d, a);
        double cdB = Cross(c, d, b);

        if (Math.Abs(abC) <= Epsilon && IsPointOnSegment(a, b, c)) return true;
        if (Math.Abs(abD) <= Epsilon && IsPointOnSegment(a, b, d)) return true;
        if (Math.Abs(cdA) <= Epsilon && IsPointOnSegment(c, d, a)) return true;
        if (Math.Abs(cdB) <= Epsilon && IsPointOnSegment(c, d, b)) return true;

        return (abC > 0d) != (abD > 0d) &&
               (cdA > 0d) != (cdB > 0d);
    }

    private static bool IsPointOnSegment(Point2D start, Point2D end, Point2D point)
    {
        if (Math.Abs(Cross(start, end, point)) > Epsilon)
        {
            return false;
        }

        return point.X >= Math.Min(start.X, end.X) - Epsilon &&
               point.X <= Math.Max(start.X, end.X) + Epsilon &&
               point.Y >= Math.Min(start.Y, end.Y) - Epsilon &&
               point.Y <= Math.Max(start.Y, end.Y) + Epsilon;
    }

    private static double Cross(Point2D a, Point2D b, Point2D c)
    {
        return ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
    }

    private static double SignedArea(IReadOnlyList<Point2D> points)
    {
        double area = 0d;
        for (int i = 0; i < points.Count; i++)
        {
            Point2D current = points[i];
            Point2D next = points[(i + 1) % points.Count];
            area += (current.X * next.Y) - (next.X * current.Y);
        }

        return area * 0.5d;
    }

    private static bool SamePoint(Point2D left, Point2D right)
    {
        double dx = left.X - right.X;
        double dy = left.Y - right.Y;
        return (dx * dx) + (dy * dy) <= Epsilon * Epsilon;
    }
}
