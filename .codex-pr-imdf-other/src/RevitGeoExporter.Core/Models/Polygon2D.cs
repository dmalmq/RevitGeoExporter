using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoExporter.Core.Models;

public sealed class Polygon2D
{
    public Polygon2D(
        IReadOnlyList<Point2D> exteriorRing,
        IReadOnlyList<IReadOnlyList<Point2D>>? interiorRings = null)
    {
        if (exteriorRing is null)
        {
            throw new ArgumentNullException(nameof(exteriorRing));
        }

        ExteriorRing = NormalizeRing(exteriorRing);
        InteriorRings = (interiorRings ?? Array.Empty<IReadOnlyList<Point2D>>())
            .Select(NormalizeRing)
            .Cast<IReadOnlyList<Point2D>>()
            .ToArray();
    }

    public IReadOnlyList<Point2D> ExteriorRing { get; }

    public IReadOnlyList<IReadOnlyList<Point2D>> InteriorRings { get; }

    public IEnumerable<Point2D> GetAllPoints()
    {
        foreach (Point2D point in ExteriorRing)
        {
            yield return point;
        }

        foreach (IReadOnlyList<Point2D> ring in InteriorRings)
        {
            foreach (Point2D point in ring)
            {
                yield return point;
            }
        }
    }

    private static IReadOnlyList<Point2D> NormalizeRing(IReadOnlyList<Point2D> ring)
    {
        if (ring.Count < 3)
        {
            throw new ArgumentException("A polygon ring requires at least 3 points.", nameof(ring));
        }

        List<Point2D> normalized = new(ring);
        Point2D first = normalized[0];
        Point2D last = normalized[normalized.Count - 1];

        if (!SamePoint(first, last))
        {
            normalized.Add(first);
        }

        if (normalized.Count < 4)
        {
            throw new ArgumentException("A polygon ring requires at least 4 points after closing.", nameof(ring));
        }

        return normalized;
    }

    private static bool SamePoint(in Point2D left, in Point2D right)
    {
        return left.X.Equals(right.X) && left.Y.Equals(right.Y);
    }
}
