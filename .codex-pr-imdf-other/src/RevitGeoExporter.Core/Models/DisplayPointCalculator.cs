using System;
using System.Collections.Generic;
using System.Globalization;

namespace RevitGeoExporter.Core.Models;

public static class DisplayPointCalculator
{
    public static Point2D CalculateCentroid(Polygon2D polygon)
    {
        if (polygon is null)
        {
            throw new ArgumentNullException(nameof(polygon));
        }

        return CalculateCentroid(polygon.ExteriorRing);
    }

    public static string ToWktPoint(Point2D point)
    {
        string x = point.X.ToString("0.###############", CultureInfo.InvariantCulture);
        string y = point.Y.ToString("0.###############", CultureInfo.InvariantCulture);
        return string.Format(CultureInfo.InvariantCulture, "POINT ({0} {1})", x, y);
    }

    private static Point2D CalculateCentroid(IReadOnlyList<Point2D> ring)
    {
        double signedAreaTwice = 0d;
        double xAccumulator = 0d;
        double yAccumulator = 0d;

        for (int i = 0; i < ring.Count - 1; i++)
        {
            Point2D current = ring[i];
            Point2D next = ring[i + 1];
            double cross = (current.X * next.Y) - (next.X * current.Y);
            signedAreaTwice += cross;
            xAccumulator += (current.X + next.X) * cross;
            yAccumulator += (current.Y + next.Y) * cross;
        }

        if (Math.Abs(signedAreaTwice) < 1e-12d)
        {
            return AveragePoint(ring);
        }

        double factor = 1d / (3d * signedAreaTwice);
        return new Point2D(xAccumulator * factor, yAccumulator * factor);
    }

    private static Point2D AveragePoint(IReadOnlyList<Point2D> points)
    {
        int count = Math.Max(1, points.Count - 1);
        double x = 0d;
        double y = 0d;
        for (int i = 0; i < count; i++)
        {
            x += points[i].X;
            y += points[i].Y;
        }

        return new Point2D(x / count, y / count);
    }
}
