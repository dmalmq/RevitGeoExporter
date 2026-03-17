using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Preview;

public sealed class FeatureBoundsCalculatorTests
{
    [Fact]
    public void FromFeatures_ComputesCombinedBoundsAcrossGeometryTypes()
    {
        ExportPolygon polygon = new(
            new Polygon2D(
                new[]
                {
                    new Point2D(0, 0),
                    new Point2D(10, 0),
                    new Point2D(10, 4),
                    new Point2D(0, 4),
                }));
        ExportLineString line = new(
            new LineString2D(
                new[]
                {
                    new Point2D(-5, 3),
                    new Point2D(2, 12),
                }));

        Bounds2D bounds = FeatureBoundsCalculator.FromFeatures(new IExportFeature[] { polygon, line });

        Assert.False(bounds.IsEmpty);
        Assert.Equal(-5d, bounds.MinX);
        Assert.Equal(0d, bounds.MinY);
        Assert.Equal(10d, bounds.MaxX);
        Assert.Equal(12d, bounds.MaxY);
    }
}
