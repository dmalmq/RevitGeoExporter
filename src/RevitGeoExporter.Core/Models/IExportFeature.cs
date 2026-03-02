using System.Collections.Generic;

namespace RevitGeoExporter.Core.Models;

public interface IExportFeature
{
    IReadOnlyDictionary<string, object?> Attributes { get; }

    IEnumerable<Point2D> GetAllPoints();
}
