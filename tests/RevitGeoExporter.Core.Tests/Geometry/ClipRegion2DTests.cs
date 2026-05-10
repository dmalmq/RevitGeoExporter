using System;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Geometry;

public sealed class ClipRegion2DTests
{
    [Fact]
    public void IntersectsBounds_RejectsBoundsInsideRotatedAabbButOutsideRegion()
    {
        ClipRegion2D region = CreateRotatedRegion(width: 20d, depth: 10d, angleRadians: Math.PI / 6d);

        bool intersects = region.IntersectsBounds(9.8d, 7.8d, 10.3d, 8.3d);

        Assert.False(intersects);
    }

    [Fact]
    public void IntersectsBounds_AcceptsBoundsInsideRotatedRegion()
    {
        ClipRegion2D region = CreateRotatedRegion(width: 20d, depth: 10d, angleRadians: Math.PI / 6d);

        bool intersects = region.IntersectsBounds(-1d, -1d, 1d, 1d);

        Assert.True(intersects);
    }

    [Fact]
    public void IntersectsBounds_AcceptsBoundsCrossingRotatedRegionEdge()
    {
        ClipRegion2D region = CreateRotatedRegion(width: 20d, depth: 10d, angleRadians: Math.PI / 6d);

        bool intersects = region.IntersectsBounds(8d, 4d, 12d, 6d);

        Assert.True(intersects);
    }

    [Fact]
    public void IntersectsBounds_AppliesTranslatedBounds()
    {
        ClipRegion2D region = ClipRegion2D.FromAxisAlignedBounds(0d, 0d, 10d, 10d);

        Assert.False(region.IntersectsBounds(12d, 2d, 14d, 4d));
        Assert.True(region.IntersectsBounds(8d, 2d, 12d, 4d));
    }

    private static ClipRegion2D CreateRotatedRegion(double width, double depth, double angleRadians)
    {
        double cos = Math.Cos(angleRadians);
        double sin = Math.Sin(angleRadians);
        ScopeBoxFootprint footprint = new(
            new Point2D(0d, 0d),
            new Point2D(cos, sin),
            new Point2D(-sin, cos),
            -width / 2d,
            -depth / 2d,
            width / 2d,
            depth / 2d);

        return ClipRegion2D.FromFootprint(footprint);
    }
}
