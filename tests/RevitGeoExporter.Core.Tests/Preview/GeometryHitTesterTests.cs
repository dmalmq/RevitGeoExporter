using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Preview;

public sealed class GeometryHitTesterTests
{
    [Fact]
    public void IsHit_ReturnsTrueForPointInsidePolygonAndFalseInsideHole()
    {
        ExportPolygon polygon = new(
            new Polygon2D(
                new[]
                {
                    new Point2D(0, 0),
                    new Point2D(10, 0),
                    new Point2D(10, 10),
                    new Point2D(0, 10),
                },
                new[]
                {
                    new[]
                    {
                        new Point2D(3, 3),
                        new Point2D(7, 3),
                        new Point2D(7, 7),
                        new Point2D(3, 7),
                    },
                }));

        Assert.True(GeometryHitTester.IsHit(polygon, new Point2D(2, 2), 0.1d));
        Assert.False(GeometryHitTester.IsHit(polygon, new Point2D(5, 5), 0.1d));
    }

    [Fact]
    public void IsHit_ReturnsTrueForLineWithinTolerance()
    {
        ExportLineString line = new(
            new LineString2D(
                new[]
                {
                    new Point2D(0, 0),
                    new Point2D(10, 0),
                }));

        Assert.True(GeometryHitTester.IsHit(line, new Point2D(5, 0.25d), 0.5d));
        Assert.False(GeometryHitTester.IsHit(line, new Point2D(5, 1.5d), 0.5d));
    }

    [Fact]
    public void FindHitIndex_PrefersLastVisibleMatch()
    {
        ExportPolygon first = new(
            new Polygon2D(
                new[]
                {
                    new Point2D(0, 0),
                    new Point2D(10, 0),
                    new Point2D(10, 10),
                    new Point2D(0, 10),
                }));
        ExportPolygon second = new(
            new Polygon2D(
                new[]
                {
                    new Point2D(0, 0),
                    new Point2D(8, 0),
                    new Point2D(8, 8),
                    new Point2D(0, 8),
                }));

        int hitIndex = GeometryHitTester.FindHitIndex(
            new IExportFeature[] { first, second },
            feature => feature,
            new Point2D(4, 4),
            0.1d);

        Assert.Equal(1, hitIndex);
    }
}
