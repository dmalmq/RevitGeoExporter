using System;
using System.Collections.Generic;

namespace RevitGeoExporter.Core.Models;

public readonly struct EscalatorFootprintProjection
{
    public EscalatorFootprintProjection(
        Point2D center,
        Point2D axis,
        double minAlong,
        double maxAlong,
        double minAcross,
        double maxAcross,
        bool usedFallbackPoints)
    {
        Center = center;
        Axis = axis;
        Perpendicular = new Point2D(-axis.Y, axis.X);
        MinAlong = minAlong;
        MaxAlong = maxAlong;
        MinAcross = minAcross;
        MaxAcross = maxAcross;
        UsedFallbackPoints = usedFallbackPoints;
    }

    public Point2D Center { get; }

    public Point2D Axis { get; }

    public Point2D Perpendicular { get; }

    public double MinAlong { get; }

    public double MaxAlong { get; }

    public double MinAcross { get; }

    public double MaxAcross { get; }

    public bool UsedFallbackPoints { get; }

    public double LengthMeters => MaxAlong - MinAlong;

    public double WidthMeters => MaxAcross - MinAcross;

    public Point2D Start => ToWorldPoint(MinAlong, 0d);

    public Point2D End => ToWorldPoint(MaxAlong, 0d);

    public Polygon2D ToPolygon(double paddingMeters = 0d)
    {
        double minAlong = MinAlong - paddingMeters;
        double maxAlong = MaxAlong + paddingMeters;
        double minAcross = MinAcross - paddingMeters;
        double maxAcross = MaxAcross + paddingMeters;

        Point2D p1 = ToWorldPoint(minAlong, minAcross);
        Point2D p2 = ToWorldPoint(maxAlong, minAcross);
        Point2D p3 = ToWorldPoint(maxAlong, maxAcross);
        Point2D p4 = ToWorldPoint(minAlong, maxAcross);
        return new Polygon2D(new[] { p1, p2, p3, p4, p1 });
    }

    private Point2D ToWorldPoint(double along, double across)
    {
        return new Point2D(
            Center.X + (Axis.X * along) + (Perpendicular.X * across),
            Center.Y + (Axis.Y * along) + (Perpendicular.Y * across));
    }
}

public static class EscalatorFootprintBuilder
{
    public static bool TryCreate(
        Point2D center,
        Point2D axis,
        IReadOnlyList<Point2D>? geometryPoints,
        double? explicitLengthMeters,
        double? explicitWidthMeters,
        IReadOnlyList<Point2D>? fallbackPoints,
        double minLengthMeters,
        double minWidthMeters,
        out EscalatorFootprintProjection footprint)
    {
        footprint = default;

        if (!TryNormalize(axis.X, axis.Y, out Point2D normalizedAxis))
        {
            return false;
        }

        Point2D perpendicular = new(-normalizedAxis.Y, normalizedAxis.X);
        bool usedFallbackPoints = false;

        if (!TryResolveAlongExtent(
                center,
                normalizedAxis,
                geometryPoints,
                explicitLengthMeters,
                fallbackPoints,
                minLengthMeters,
                ref usedFallbackPoints,
                out double minAlong,
                out double maxAlong))
        {
            return false;
        }

        if (!TryResolveAcrossExtent(
                center,
                perpendicular,
                geometryPoints,
                explicitWidthMeters,
                fallbackPoints,
                minWidthMeters,
                ref usedFallbackPoints,
                out double minAcross,
                out double maxAcross))
        {
            return false;
        }

        if ((maxAlong - minAlong) < minLengthMeters ||
            (maxAcross - minAcross) < minWidthMeters)
        {
            return false;
        }

        footprint = new EscalatorFootprintProjection(
            center,
            normalizedAxis,
            minAlong,
            maxAlong,
            minAcross,
            maxAcross,
            usedFallbackPoints);
        return true;
    }

    private static bool TryResolveAlongExtent(
        Point2D center,
        Point2D axis,
        IReadOnlyList<Point2D>? geometryPoints,
        double? explicitLengthMeters,
        IReadOnlyList<Point2D>? fallbackPoints,
        double minimumLengthMeters,
        ref bool usedFallbackPoints,
        out double minAlong,
        out double maxAlong)
    {
        minAlong = 0d;
        maxAlong = 0d;

        if (explicitLengthMeters.HasValue && explicitLengthMeters.Value >= minimumLengthMeters)
        {
            double halfLength = explicitLengthMeters.Value * 0.5d;
            minAlong = -halfLength;
            maxAlong = halfLength;
            return true;
        }

        if (TryGetAxisExtent(geometryPoints, center, axis, minimumLengthMeters, out minAlong, out maxAlong))
        {
            return true;
        }

        if (TryGetAxisExtent(fallbackPoints, center, axis, minimumLengthMeters, out minAlong, out maxAlong))
        {
            usedFallbackPoints = true;
            return true;
        }

        return false;
    }

    private static bool TryResolveAcrossExtent(
        Point2D center,
        Point2D perpendicular,
        IReadOnlyList<Point2D>? geometryPoints,
        double? explicitWidthMeters,
        IReadOnlyList<Point2D>? fallbackPoints,
        double minimumWidthMeters,
        ref bool usedFallbackPoints,
        out double minAcross,
        out double maxAcross)
    {
        minAcross = 0d;
        maxAcross = 0d;

        if (TryGetAxisExtent(geometryPoints, center, perpendicular, minimumWidthMeters, out minAcross, out maxAcross))
        {
            return true;
        }

        if (explicitWidthMeters.HasValue && explicitWidthMeters.Value >= minimumWidthMeters)
        {
            double halfWidth = explicitWidthMeters.Value * 0.5d;
            minAcross = -halfWidth;
            maxAcross = halfWidth;
            return true;
        }

        if (TryGetAxisExtent(fallbackPoints, center, perpendicular, minimumWidthMeters, out minAcross, out maxAcross))
        {
            usedFallbackPoints = true;
            return true;
        }

        return false;
    }

    private static bool TryGetAxisExtent(
        IReadOnlyList<Point2D>? points,
        Point2D center,
        Point2D axis,
        double minimumExtentMeters,
        out double min,
        out double max)
    {
        min = double.MaxValue;
        max = double.MinValue;

        if (points == null || points.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < points.Count; i++)
        {
            double dx = points[i].X - center.X;
            double dy = points[i].Y - center.Y;
            double projection = (dx * axis.X) + (dy * axis.Y);
            if (projection < min)
            {
                min = projection;
            }

            if (projection > max)
            {
                max = projection;
            }
        }

        return (max - min) >= minimumExtentMeters;
    }

    private static bool TryNormalize(double x, double y, out Point2D normalized)
    {
        normalized = default;
        double length = Math.Sqrt((x * x) + (y * y));
        if (length <= 1e-9d)
        {
            return false;
        }

        normalized = new Point2D(x / length, y / length);
        return true;
    }
}
