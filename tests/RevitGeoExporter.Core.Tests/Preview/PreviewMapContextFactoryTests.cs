using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Preview;

public sealed class PreviewMapContextFactoryTests
{
    private const string Wgs84Wkt =
        "GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]]," +
        "PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433],AUTHORITY[\"EPSG\",\"4326\"]]";

    [Fact]
    public void Create_SharedCoordinates_UsesResolvedSourceCrs()
    {
        PreviewMapContext context = PreviewMapContextFactory.Create(
            CoordinateExportMode.SharedCoordinates,
            targetEpsg: 3857,
            sourceEpsg: 6677,
            sourceCoordinateSystemId: "EPSG:6677",
            sourceCoordinateSystemDefinition: string.Empty);

        Assert.True(context.CanShowBasemap);
        Assert.Equal(6677, context.OutputEpsg);
        Assert.Contains("6677", context.OutputCrsLabel);
        Assert.NotNull(context.SourceCoordinateSystem);
        Assert.NotNull(context.OutputCoordinateSystem);
        Assert.NotNull(context.DisplayCoordinateSystem);
        Assert.Same(context.SourceCoordinateSystem, context.OutputCoordinateSystem);
        Assert.Equal(string.Empty, context.UnavailableReason);
    }

    [Fact]
    public void Create_ConvertMode_UsesTargetCrs()
    {
        PreviewMapContext context = PreviewMapContextFactory.Create(
            CoordinateExportMode.ConvertToTargetCrs,
            targetEpsg: 3857,
            sourceEpsg: 6677,
            sourceCoordinateSystemId: "EPSG:6677",
            sourceCoordinateSystemDefinition: string.Empty);

        Assert.True(context.CanShowBasemap);
        Assert.Equal(3857, context.OutputEpsg);
        Assert.Contains("3857", context.OutputCrsLabel);
        Assert.NotNull(context.SourceCoordinateSystem);
        Assert.NotNull(context.OutputCoordinateSystem);
    }

    [Fact]
    public void Create_SharedCoordinatesWithoutResolvableSource_DisablesBasemap()
    {
        PreviewMapContext context = PreviewMapContextFactory.Create(
            CoordinateExportMode.SharedCoordinates,
            targetEpsg: 3857,
            sourceEpsg: null,
            sourceCoordinateSystemId: string.Empty,
            sourceCoordinateSystemDefinition: string.Empty);

        Assert.False(context.CanShowBasemap);
        Assert.NotEmpty(context.UnavailableReason);
        Assert.Null(context.SourceCoordinateSystem);
        Assert.Null(context.OutputCoordinateSystem);
    }

    [Fact]
    public void Create_SharedCoordinates_PrefersCoordinateSystemIdOverConflictingWktForPreviewProjection()
    {
        PreviewMapContext context = PreviewMapContextFactory.Create(
            CoordinateExportMode.SharedCoordinates,
            targetEpsg: 3857,
            sourceEpsg: null,
            sourceCoordinateSystemId: "EPSG:6677",
            sourceCoordinateSystemDefinition: Wgs84Wkt);

        Assert.True(context.CanShowBasemap);
        Assert.NotNull(context.SourceCoordinateSystem);
        Assert.NotNull(context.OutputCoordinateSystem);
        Assert.NotNull(context.DisplayCoordinateSystem);

        Point2D transformed = CoordinateSystemCatalog.ReprojectPoint(
            new Point2D(0d, 0d),
            context.OutputCoordinateSystem!,
            context.DisplayCoordinateSystem!);

        Assert.InRange(transformed.X, 15500000d, 15650000d);
        Assert.InRange(transformed.Y, 4200000d, 4400000d);
    }

    [Fact]
    public void Create_ConvertModeWithoutResolvableSource_DisablesBasemap()
    {
        PreviewMapContext context = PreviewMapContextFactory.Create(
            CoordinateExportMode.ConvertToTargetCrs,
            targetEpsg: 6677,
            sourceEpsg: null,
            sourceCoordinateSystemId: string.Empty,
            sourceCoordinateSystemDefinition: string.Empty);

        Assert.False(context.CanShowBasemap);
        Assert.Equal(6677, context.OutputEpsg);
        Assert.Null(context.SourceCoordinateSystem);
        Assert.NotEmpty(context.UnavailableReason);
    }

    [Fact]
    public void Create_ConvertModeWithoutResolvableSource_DisablesBasemapWithSpecificReason()
    {
        PreviewMapContext context = PreviewMapContextFactory.Create(
            CoordinateExportMode.ConvertToTargetCrs,
            targetEpsg: 4326,
            sourceEpsg: null,
            sourceCoordinateSystemId: string.Empty,
            sourceCoordinateSystemDefinition: string.Empty);

        Assert.False(context.CanShowBasemap);
        Assert.Null(context.SourceCoordinateSystem);
        Assert.Null(context.OutputCoordinateSystem);
        Assert.Equal(
            PreviewMapContextFactory.ConvertModeMissingSourceReason,
            context.UnavailableReason);
    }
}
