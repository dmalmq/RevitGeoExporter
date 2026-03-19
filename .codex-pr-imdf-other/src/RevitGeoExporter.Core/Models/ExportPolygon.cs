using System;
using System.Collections.Generic;

namespace RevitGeoExporter.Core.Models;

public sealed class ExportPolygon : IExportFeature
{
    public ExportPolygon(Polygon2D polygon, IReadOnlyDictionary<string, object?>? attributes = null)
        : this(new[] { polygon ?? throw new ArgumentNullException(nameof(polygon)) }, attributes)
    {
    }

    public ExportPolygon(
        IReadOnlyList<Polygon2D> polygons,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        if (polygons is null)
        {
            throw new ArgumentNullException(nameof(polygons));
        }

        if (polygons.Count == 0)
        {
            throw new ArgumentException("At least one polygon is required.", nameof(polygons));
        }

        Polygons = polygons;
        Attributes = attributes ?? new Dictionary<string, object?>();
    }

    public IReadOnlyList<Polygon2D> Polygons { get; }

    public IReadOnlyDictionary<string, object?> Attributes { get; }

    public IEnumerable<Point2D> GetAllPoints()
    {
        foreach (Polygon2D polygon in Polygons)
        {
            foreach (Point2D point in polygon.GetAllPoints())
            {
                yield return point;
            }
        }
    }
}
