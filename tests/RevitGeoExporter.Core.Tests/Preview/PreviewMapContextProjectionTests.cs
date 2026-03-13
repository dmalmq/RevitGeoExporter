using System;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Preview;

public sealed class PreviewMapContextProjectionTests
{
    private const double Tolerance = 0.000001d;

    [Fact]
    public void ProjectFeatureForDisplay_ConvertMode_UsesSourceThenTargetProjectionChain()
    {
        PreviewMapContext context = PreviewMapContextFactory.Create(
            CoordinateExportMode.ConvertToTargetCrs,
            targetEpsg: 4326,
            sourceEpsg: 6677,
            sourceCoordinateSystemId: "EPSG:6677",
            sourceCoordinateSystemDefinition: string.Empty);

        Assert.True(context.CanShowBasemap);

        ExportLineString sourceFeature = new(
            new LineString2D(new[]
            {
                new Point2D(0d, 0d),
                new Point2D(1d, 1d),
            }));

        ExportLineString actual = Assert.IsType<ExportLineString>(
            context.ProjectFeatureForDisplay(sourceFeature));

        Assert.True(CoordinateSystemCatalog.TryCreateFromEpsg(6677, out var sourceCoordinateSystem));
        Assert.True(CoordinateSystemCatalog.TryCreateFromEpsg(4326, out var outputCoordinateSystem));
        Assert.True(CoordinateSystemCatalog.TryCreateWebMercator(out var displayCoordinateSystem));

        ExportLineString expected = Assert.IsType<ExportLineString>(
            CoordinateSystemCatalog.ReprojectFeature(
                CoordinateSystemCatalog.ReprojectFeature(
                    sourceFeature,
                    sourceCoordinateSystem!,
                    outputCoordinateSystem!),
                outputCoordinateSystem!,
                displayCoordinateSystem!));

        ExportLineString wrongPath = Assert.IsType<ExportLineString>(
            CoordinateSystemCatalog.ReprojectFeature(
                sourceFeature,
                outputCoordinateSystem!,
                displayCoordinateSystem!));

        AssertPointClose(expected.LineString.Points[0], actual.LineString.Points[0]);
        AssertPointClose(expected.LineString.Points[1], actual.LineString.Points[1]);

        Assert.True(
            Math.Abs(actual.LineString.Points[0].X - wrongPath.LineString.Points[0].X) > 1_000_000d,
            "Preview projection should not treat raw source geometry as if it were already in the target CRS.");
    }

    private static void AssertPointClose(Point2D expected, Point2D actual)
    {
        Assert.InRange(actual.X, expected.X - Tolerance, expected.X + Tolerance);
        Assert.InRange(actual.Y, expected.Y - Tolerance, expected.Y + Tolerance);
    }
}
