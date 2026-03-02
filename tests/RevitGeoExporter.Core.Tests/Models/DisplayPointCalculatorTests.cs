using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Models;

public sealed class DisplayPointCalculatorTests
{
    private const double Tolerance = 0.000001d;

    [Fact]
    public void Centroid_ForRectangle_IsCenterPoint()
    {
        Polygon2D polygon = new(
            new[]
            {
                new Point2D(0, 0),
                new Point2D(10, 0),
                new Point2D(10, 6),
                new Point2D(0, 6),
            });

        Point2D centroid = DisplayPointCalculator.CalculateCentroid(polygon);

        Assert.InRange(centroid.X, 5d - Tolerance, 5d + Tolerance);
        Assert.InRange(centroid.Y, 3d - Tolerance, 3d + Tolerance);
    }

    [Fact]
    public void ToWktPoint_FormatsUsingInvariantCulture()
    {
        string wkt = DisplayPointCalculator.ToWktPoint(new Point2D(12.5, 34.25));

        Assert.Equal("POINT (12.5 34.25)", wkt);
    }
}
