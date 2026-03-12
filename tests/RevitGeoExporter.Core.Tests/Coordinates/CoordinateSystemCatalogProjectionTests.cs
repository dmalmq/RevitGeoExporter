using System.Linq;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Coordinates;

public sealed class CoordinateSystemCatalogProjectionTests
{
    [Fact]
    public void ReprojectFeature_ConvertsJgd2011ZoneIxToWebMercator()
    {
        Assert.True(CoordinateSystemCatalog.TryCreateFromEpsg(6677, out var source));
        Assert.True(CoordinateSystemCatalog.TryCreateWebMercator(out var target));

        ExportLineString feature = new(
            new LineString2D(new[]
            {
                new Point2D(0d, 0d),
                new Point2D(100d, 100d),
            }));

        ExportLineString transformed = Assert.IsType<ExportLineString>(
            CoordinateSystemCatalog.ReprojectFeature(feature, source!, target!));

        Point2D first = transformed.LineString.Points.First();
        Assert.InRange(first.X, 15500000d, 15650000d);
        Assert.InRange(first.Y, 4200000d, 4400000d);
        Assert.All(transformed.LineString.Points, point =>
        {
            Assert.False(double.IsNaN(point.X));
            Assert.False(double.IsNaN(point.Y));
            Assert.False(double.IsInfinity(point.X));
            Assert.False(double.IsInfinity(point.Y));
        });
    }
}