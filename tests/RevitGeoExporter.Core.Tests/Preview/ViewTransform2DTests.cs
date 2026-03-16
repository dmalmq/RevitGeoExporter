using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Preview;

public sealed class ViewTransform2DTests
{
    private const double Tolerance = 0.000001d;

    [Fact]
    public void Fit_CentersBoundsWithinViewport()
    {
        Bounds2D bounds = new(0d, 0d, 100d, 50d);

        ViewTransform2D transform = ViewTransform2D.Fit(bounds, 200d, 100d, paddingPixels: 0d);
        Point2D center = transform.WorldToScreen(new Point2D(50d, 25d));

        Assert.InRange(center.X, 100d - Tolerance, 100d + Tolerance);
        Assert.InRange(center.Y, 50d - Tolerance, 50d + Tolerance);
        Assert.InRange(transform.PixelsPerWorldUnit, 2d - Tolerance, 2d + Tolerance);
    }

    [Fact]
    public void ZoomAt_PreservesAnchorWorldPoint()
    {
        ViewTransform2D transform = ViewTransform2D.Fit(new Bounds2D(0d, 0d, 20d, 20d), 200d, 200d, paddingPixels: 0d);
        Point2D anchorScreen = new(40d, 60d);
        Point2D anchorWorldBefore = transform.ScreenToWorld(anchorScreen);

        ViewTransform2D zoomed = transform.ZoomAt(2d, anchorScreen);
        Point2D anchorWorldAfter = zoomed.ScreenToWorld(anchorScreen);

        Assert.InRange(anchorWorldAfter.X, anchorWorldBefore.X - Tolerance, anchorWorldBefore.X + Tolerance);
        Assert.InRange(anchorWorldAfter.Y, anchorWorldBefore.Y - Tolerance, anchorWorldBefore.Y + Tolerance);
    }

    [Fact]
    public void Pan_ShiftsWorldCenterByScreenDelta()
    {
        ViewTransform2D transform = new(200d, 100d, 0d, 0d, 10d);

        ViewTransform2D panned = transform.Pan(20d, -30d);

        Assert.InRange(panned.WorldCenterX, -2d - Tolerance, -2d + Tolerance);
        Assert.InRange(panned.WorldCenterY, -3d - Tolerance, -3d + Tolerance);
    }
}
