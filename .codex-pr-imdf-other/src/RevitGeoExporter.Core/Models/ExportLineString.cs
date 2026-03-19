using System;
using System.Collections.Generic;

namespace RevitGeoExporter.Core.Models;

public sealed class ExportLineString : IExportFeature
{
    public ExportLineString(
        LineString2D lineString,
        IReadOnlyDictionary<string, object?>? attributes = null)
    {
        LineString = lineString ?? throw new ArgumentNullException(nameof(lineString));
        Attributes = attributes ?? new Dictionary<string, object?>();
    }

    public LineString2D LineString { get; }

    public IReadOnlyDictionary<string, object?> Attributes { get; }

    public IEnumerable<Point2D> GetAllPoints()
    {
        return LineString.Points;
    }
}
