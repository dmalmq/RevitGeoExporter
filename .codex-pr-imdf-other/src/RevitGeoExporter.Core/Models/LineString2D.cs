using System;
using System.Collections.Generic;

namespace RevitGeoExporter.Core.Models;

public sealed class LineString2D
{
    public LineString2D(IReadOnlyList<Point2D> points)
    {
        if (points is null)
        {
            throw new ArgumentNullException(nameof(points));
        }

        if (points.Count < 2)
        {
            throw new ArgumentException("A LineString requires at least 2 points.", nameof(points));
        }

        Points = points;
    }

    public IReadOnlyList<Point2D> Points { get; }
}
