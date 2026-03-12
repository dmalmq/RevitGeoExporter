using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Preview;

public sealed class PreviewMapContextFactoryTests
{
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
}