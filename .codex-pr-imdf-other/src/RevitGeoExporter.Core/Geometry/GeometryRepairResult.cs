using System.Collections.Generic;

namespace RevitGeoExporter.Core.Geometry;

public sealed class GeometryRepairResult
{
    public int DroppedPolygons { get; set; }

    public int SimplifiedPolygons { get; set; }

    public int DroppedOpenings { get; set; }

    public int SimplifiedDetails { get; set; }

    public List<string> Notes { get; set; } = new();

    public void MergeFrom(GeometryRepairResult? other)
    {
        if (other == null)
        {
            return;
        }

        DroppedPolygons += other.DroppedPolygons;
        SimplifiedPolygons += other.SimplifiedPolygons;
        DroppedOpenings += other.DroppedOpenings;
        SimplifiedDetails += other.SimplifiedDetails;
        Notes.AddRange(other.Notes);
    }
}
