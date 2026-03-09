using System;

namespace RevitGeoExporter.Core.Geometry;

public sealed class GeometryRepairOptions
{
    public bool Enabled { get; set; }

    public double MinimumPolygonAreaSquareMeters { get; set; } = 0.01d;

    public double MinimumOpeningLengthMeters { get; set; } = 0.10d;

    public double SimplifyToleranceMeters { get; set; } = 0d;

    public double OpeningSnapDistanceMeters { get; set; } = 0.20d;

    public double ElevatorOpeningSnapDistanceMeters { get; set; } = 0.20d;

    public double MergeNearbyBoundaryThresholdMeters { get; set; } = 0.15d;

    public GeometryRepairOptions Clone()
    {
        return new GeometryRepairOptions
        {
            Enabled = Enabled,
            MinimumPolygonAreaSquareMeters = MinimumPolygonAreaSquareMeters,
            MinimumOpeningLengthMeters = MinimumOpeningLengthMeters,
            SimplifyToleranceMeters = SimplifyToleranceMeters,
            OpeningSnapDistanceMeters = OpeningSnapDistanceMeters,
            ElevatorOpeningSnapDistanceMeters = ElevatorOpeningSnapDistanceMeters,
            MergeNearbyBoundaryThresholdMeters = MergeNearbyBoundaryThresholdMeters,
        };
    }

    public GeometryRepairOptions GetEffectiveOptions()
    {
        GeometryRepairOptions clone = Clone();
        if (!clone.Enabled)
        {
            clone.MinimumPolygonAreaSquareMeters = 0.01d;
            clone.MinimumOpeningLengthMeters = 0.10d;
            clone.SimplifyToleranceMeters = 0d;
            clone.OpeningSnapDistanceMeters = 0.20d;
            clone.ElevatorOpeningSnapDistanceMeters = 0.20d;
            clone.MergeNearbyBoundaryThresholdMeters = 0.15d;
        }

        clone.MinimumPolygonAreaSquareMeters = Math.Max(0d, clone.MinimumPolygonAreaSquareMeters);
        clone.MinimumOpeningLengthMeters = Math.Max(0.01d, clone.MinimumOpeningLengthMeters);
        clone.SimplifyToleranceMeters = Math.Max(0d, clone.SimplifyToleranceMeters);
        clone.OpeningSnapDistanceMeters = Math.Max(0.05d, clone.OpeningSnapDistanceMeters);
        clone.ElevatorOpeningSnapDistanceMeters = Math.Max(0.05d, clone.ElevatorOpeningSnapDistanceMeters);
        clone.MergeNearbyBoundaryThresholdMeters = Math.Max(0d, clone.MergeNearbyBoundaryThresholdMeters);
        return clone;
    }
}
