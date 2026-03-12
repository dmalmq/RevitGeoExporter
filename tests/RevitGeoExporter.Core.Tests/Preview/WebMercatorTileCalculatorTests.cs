using RevitGeoExporter.Core.Preview;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Preview;

public sealed class WebMercatorTileCalculatorTests
{
    private const double Tolerance = 0.000001d;

    [Fact]
    public void GetZoomLevel_ClampsToConfiguredRange()
    {
        Assert.Equal(WebMercatorTileCalculator.MinZoom, WebMercatorTileCalculator.GetZoomLevel(0d));
        Assert.Equal(WebMercatorTileCalculator.MaxZoom, WebMercatorTileCalculator.GetZoomLevel(1000000d));
    }

    [Fact]
    public void GetVisibleTiles_FullExtentAtZoomZero_ReturnsSingleWorldTile()
    {
        Bounds2D bounds = new(
            -WebMercatorTileCalculator.MaxExtent,
            -WebMercatorTileCalculator.MaxExtent,
            WebMercatorTileCalculator.MaxExtent,
            WebMercatorTileCalculator.MaxExtent);

        var tiles = WebMercatorTileCalculator.GetVisibleTiles(bounds, zoom: 0, paddingTiles: 0);

        WebMercatorTile tile = Assert.Single(tiles);
        Assert.Equal(0, tile.Zoom);
        Assert.Equal(0, tile.X);
        Assert.Equal(0, tile.Y);
    }

    [Fact]
    public void GetTileBounds_ZoomZero_MatchesWorldExtent()
    {
        Bounds2D bounds = WebMercatorTileCalculator.GetTileBounds(0, 0, 0);

        Assert.InRange(bounds.MinX, -WebMercatorTileCalculator.MaxExtent - Tolerance, -WebMercatorTileCalculator.MaxExtent + Tolerance);
        Assert.InRange(bounds.MinY, -WebMercatorTileCalculator.MaxExtent - Tolerance, -WebMercatorTileCalculator.MaxExtent + Tolerance);
        Assert.InRange(bounds.MaxX, WebMercatorTileCalculator.MaxExtent - Tolerance, WebMercatorTileCalculator.MaxExtent + Tolerance);
        Assert.InRange(bounds.MaxY, WebMercatorTileCalculator.MaxExtent - Tolerance, WebMercatorTileCalculator.MaxExtent + Tolerance);
    }
}