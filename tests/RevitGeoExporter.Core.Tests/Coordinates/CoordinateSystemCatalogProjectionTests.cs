using System.Linq;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Coordinates;

public sealed class CoordinateSystemCatalogProjectionTests
{
    private const string Wgs84Wkt =
        "GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]]," +
        "PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433],AUTHORITY[\"EPSG\",\"4326\"]]";

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

    [Fact]
    public void TryCreateSourceCoordinateSystem_PrefersResolvedEpsgOverConflictingWkt()
    {
        Assert.True(
            CoordinateSystemCatalog.TryCreateSourceCoordinateSystem(
                siteCoordinateSystemDefinition: Wgs84Wkt,
                siteCoordinateSystemId: string.Empty,
                resolvedSourceEpsg: 6677,
                out var source,
                out var failureReason));
        Assert.Equal(string.Empty, failureReason);
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
    }
}
