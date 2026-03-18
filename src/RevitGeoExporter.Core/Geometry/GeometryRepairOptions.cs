using System;

namespace RevitGeoExporter.Core.Geometry;

public sealed class GeometryRepairOptions
{
    /// <summary>
    /// Enables geometry repair and normalization rules. When disabled, defaults are still applied as guardrails.
    /// </summary>
    public bool Enabled { get; set; }

    public double MinimumPolygonAreaSquareMeters { get; set; } = 0.01d;

    public double MinimumOpeningLengthMeters { get; set; } = 0.10d;

    public double SimplifyToleranceMeters { get; set; } = 0d;

    public double OpeningSnapDistanceMeters { get; set; } = 0.20d;

    public double ElevatorOpeningSnapDistanceMeters { get; set; } = 0.20d;

    /// <summary>
    /// Unit-level gap closing threshold. Defaults to 0.15 m (15 cm).
    /// At coarse map scales, this can still leave visible internal voids between nearby unit boundaries.
    /// </summary>
    public double MergeNearbyBoundaryThresholdMeters { get; set; } = 0.15d;

    /// <summary>
    /// Maximum retained hole size for unit polygons. Defaults to 0.05 m (5 cm).
    /// At coarse map scales, holes this size can still appear as visible internal voids.
    /// </summary>
    public double MaxHoleSizeMeters { get; set; } = 0.05d;

    /// <summary>
    /// Level-boundary gap closing threshold. Defaults to 0.20 m (20 cm) so level boundaries can be stricter than unit output.
    /// </summary>
    public double LevelBoundaryGapClosingThresholdMeters { get; set; } = 0.20d;

    /// <summary>
    /// Maximum retained hole size for level boundaries. Defaults to 0.10 m (10 cm) so level boundaries can suppress more interior voids than units.
    /// </summary>
    public double LevelBoundaryMaxHoleSizeMeters { get; set; } = 0.10d;

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
            MaxHoleSizeMeters = MaxHoleSizeMeters,
            LevelBoundaryGapClosingThresholdMeters = LevelBoundaryGapClosingThresholdMeters,
            LevelBoundaryMaxHoleSizeMeters = LevelBoundaryMaxHoleSizeMeters,
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
            clone.MaxHoleSizeMeters = 0.05d;
            clone.LevelBoundaryGapClosingThresholdMeters = 0.20d;
            clone.LevelBoundaryMaxHoleSizeMeters = 0.10d;
        }

        clone.MinimumPolygonAreaSquareMeters = Math.Max(0d, clone.MinimumPolygonAreaSquareMeters);
        clone.MinimumOpeningLengthMeters = Math.Max(0.01d, clone.MinimumOpeningLengthMeters);
        clone.SimplifyToleranceMeters = Math.Max(0d, clone.SimplifyToleranceMeters);
        clone.OpeningSnapDistanceMeters = Math.Max(0.05d, clone.OpeningSnapDistanceMeters);
        clone.ElevatorOpeningSnapDistanceMeters = Math.Max(0.05d, clone.ElevatorOpeningSnapDistanceMeters);
        clone.MergeNearbyBoundaryThresholdMeters = Math.Max(0d, clone.MergeNearbyBoundaryThresholdMeters);
        clone.MaxHoleSizeMeters = Math.Max(0d, clone.MaxHoleSizeMeters);
        clone.LevelBoundaryGapClosingThresholdMeters = Math.Max(0d, clone.LevelBoundaryGapClosingThresholdMeters);
        clone.LevelBoundaryMaxHoleSizeMeters = Math.Max(0d, clone.LevelBoundaryMaxHoleSizeMeters);
        return clone;
    }
}
