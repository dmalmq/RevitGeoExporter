using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Geometry;

public static class ScopeBoxFootprintBuilder
{
    private const double Epsilon = 1e-9d;
    private const double DirectionTolerance = 1e-6d;

    public static bool TryBuild(
        IReadOnlyList<ScopeBoxEdge3D>? edges,
        double minUsefulExtent,
        out ScopeBoxFootprint footprint)
    {
        footprint = default;
        if (edges == null || edges.Count == 0)
        {
            return false;
        }

        List<Point3> points = CollectUniquePoints(edges);
        if (points.Count < 4)
        {
            return false;
        }

        ScopeBoxEdge3D? referenceEdge = FindReferenceHorizontalEdge(edges);
        if (referenceEdge == null)
        {
            return false;
        }

        ScopeBoxEdge3D edge = referenceEdge.Value;
        double dx = edge.EndX - edge.StartX;
        double dy = edge.EndY - edge.StartY;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < Epsilon)
        {
            return false;
        }

        Point2D xBasis = new(dx / length, dy / length);
        Point2D yBasis = new(-xBasis.Y, xBasis.X);
        Point2D origin = CalculateCenter(points);

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        foreach (Point3 point in points)
        {
            double relativeX = point.X - origin.X;
            double relativeY = point.Y - origin.Y;
            double localX = (relativeX * xBasis.X) + (relativeY * xBasis.Y);
            double localY = (relativeX * yBasis.X) + (relativeY * yBasis.Y);

            if (localX < minX) minX = localX;
            if (localY < minY) minY = localY;
            if (localX > maxX) maxX = localX;
            if (localY > maxY) maxY = localY;
        }

        double requiredExtent = Math.Max(0d, minUsefulExtent);
        if (maxX - minX < requiredExtent || maxY - minY < requiredExtent)
        {
            return false;
        }

        footprint = new ScopeBoxFootprint(origin, xBasis, yBasis, minX, minY, maxX, maxY);
        return true;
    }

    private static ScopeBoxEdge3D? FindReferenceHorizontalEdge(IReadOnlyList<ScopeBoxEdge3D> edges)
    {
        double bestLength = 0d;
        ScopeBoxEdge3D? best = null;

        foreach (ScopeBoxEdge3D edge in edges)
        {
            double dx = edge.EndX - edge.StartX;
            double dy = edge.EndY - edge.StartY;
            double dz = edge.EndZ - edge.StartZ;
            double xyLength = Math.Sqrt((dx * dx) + (dy * dy));
            double length = Math.Sqrt((xyLength * xyLength) + (dz * dz));
            if (xyLength < Epsilon || length < Epsilon)
            {
                continue;
            }

            if (Math.Abs(dz) > Math.Max(Epsilon, xyLength * DirectionTolerance))
            {
                continue;
            }

            if (xyLength > bestLength)
            {
                bestLength = xyLength;
                best = edge;
            }
        }

        return best;
    }

    private static List<Point3> CollectUniquePoints(IReadOnlyList<ScopeBoxEdge3D> edges)
    {
        List<Point3> points = new();
        foreach (ScopeBoxEdge3D edge in edges)
        {
            AddUnique(points, new Point3(edge.StartX, edge.StartY, edge.StartZ));
            AddUnique(points, new Point3(edge.EndX, edge.EndY, edge.EndZ));
        }

        return points;
    }

    private static void AddUnique(List<Point3> points, Point3 point)
    {
        foreach (Point3 existing in points)
        {
            if (DistanceSquared(existing, point) <= Epsilon * Epsilon)
            {
                return;
            }
        }

        points.Add(point);
    }

    private static Point2D CalculateCenter(IReadOnlyList<Point3> points)
    {
        double x = 0d;
        double y = 0d;
        foreach (Point3 point in points)
        {
            x += point.X;
            y += point.Y;
        }

        return new Point2D(x / points.Count, y / points.Count);
    }

    private static double DistanceSquared(Point3 left, Point3 right)
    {
        double dx = left.X - right.X;
        double dy = left.Y - right.Y;
        double dz = left.Z - right.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }

    private readonly struct Point3
    {
        public Point3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }
}
