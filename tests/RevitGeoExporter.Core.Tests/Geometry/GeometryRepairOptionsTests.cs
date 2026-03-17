using RevitGeoExporter.Core.Geometry;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Geometry;

public sealed class GeometryRepairOptionsTests
{
    [Fact]
    public void GetEffectiveOptions_WhenDisabled_UsesSafeDefaults()
    {
        GeometryRepairOptions options = new()
        {
            Enabled = false,
            MinimumPolygonAreaSquareMeters = 9d,
            MinimumOpeningLengthMeters = 9d,
            SimplifyToleranceMeters = 9d,
            OpeningSnapDistanceMeters = 9d,
            ElevatorOpeningSnapDistanceMeters = 9d,
            MergeNearbyBoundaryThresholdMeters = 9d,
            MaxHoleSizeMeters = 9d,
        };

        GeometryRepairOptions effective = options.GetEffectiveOptions();

        Assert.Equal(0.01d, effective.MinimumPolygonAreaSquareMeters);
        Assert.Equal(0.10d, effective.MinimumOpeningLengthMeters);
        Assert.Equal(0d, effective.SimplifyToleranceMeters);
        Assert.Equal(0.20d, effective.OpeningSnapDistanceMeters);
        Assert.Equal(0.20d, effective.ElevatorOpeningSnapDistanceMeters);
        Assert.Equal(0.15d, effective.MergeNearbyBoundaryThresholdMeters);
        Assert.Equal(0.05d, effective.MaxHoleSizeMeters);
    }

    [Fact]
    public void GetEffectiveOptions_ClampsNegativeHoleSizeToZero()
    {
        GeometryRepairOptions options = new()
        {
            Enabled = true,
            MaxHoleSizeMeters = -0.2d,
        };

        GeometryRepairOptions effective = options.GetEffectiveOptions();

        Assert.Equal(0d, effective.MaxHoleSizeMeters);
    }
}