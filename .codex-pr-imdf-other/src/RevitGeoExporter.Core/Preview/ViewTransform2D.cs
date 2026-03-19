using System;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Preview;

public readonly struct ViewTransform2D
{
    private const double MinPixelsPerWorldUnit = 0.000001d;
    private const double MaxPixelsPerWorldUnit = 1000000d;

    public ViewTransform2D(
        double viewportWidth,
        double viewportHeight,
        double worldCenterX,
        double worldCenterY,
        double pixelsPerWorldUnit)
    {
        ViewportWidth = Math.Max(1d, viewportWidth);
        ViewportHeight = Math.Max(1d, viewportHeight);
        WorldCenterX = worldCenterX;
        WorldCenterY = worldCenterY;
        PixelsPerWorldUnit = ClampScale(pixelsPerWorldUnit);
    }

    public double ViewportWidth { get; }

    public double ViewportHeight { get; }

    public double WorldCenterX { get; }

    public double WorldCenterY { get; }

    public double PixelsPerWorldUnit { get; }

    public static ViewTransform2D Fit(Bounds2D bounds, double viewportWidth, double viewportHeight, double paddingPixels = 24d)
    {
        double width = Math.Max(1d, viewportWidth);
        double height = Math.Max(1d, viewportHeight);
        if (bounds.IsEmpty)
        {
            return new ViewTransform2D(width, height, 0d, 0d, 1d);
        }

        double padding = Math.Max(0d, paddingPixels);
        double availableWidth = Math.Max(1d, width - (padding * 2d));
        double availableHeight = Math.Max(1d, height - (padding * 2d));

        double contentWidth = bounds.Width > 0d ? bounds.Width : 1d;
        double contentHeight = bounds.Height > 0d ? bounds.Height : 1d;
        double scale = Math.Min(availableWidth / contentWidth, availableHeight / contentHeight);

        Point2D center = bounds.Center;
        return new ViewTransform2D(width, height, center.X, center.Y, scale);
    }

    public ViewTransform2D WithViewportSize(double viewportWidth, double viewportHeight)
    {
        return new ViewTransform2D(viewportWidth, viewportHeight, WorldCenterX, WorldCenterY, PixelsPerWorldUnit);
    }

    public ViewTransform2D Pan(double deltaScreenX, double deltaScreenY)
    {
        double deltaWorldX = -deltaScreenX / PixelsPerWorldUnit;
        double deltaWorldY = deltaScreenY / PixelsPerWorldUnit;
        return new ViewTransform2D(
            ViewportWidth,
            ViewportHeight,
            WorldCenterX + deltaWorldX,
            WorldCenterY + deltaWorldY,
            PixelsPerWorldUnit);
    }

    public ViewTransform2D ZoomAt(double zoomFactor, Point2D screenAnchor)
    {
        if (zoomFactor <= 0d || double.IsNaN(zoomFactor) || double.IsInfinity(zoomFactor))
        {
            return this;
        }

        Point2D anchorWorld = ScreenToWorld(screenAnchor);
        double newScale = ClampScale(PixelsPerWorldUnit * zoomFactor);
        double newCenterX = anchorWorld.X - ((screenAnchor.X - (ViewportWidth * 0.5d)) / newScale);
        double newCenterY = anchorWorld.Y + ((screenAnchor.Y - (ViewportHeight * 0.5d)) / newScale);
        return new ViewTransform2D(ViewportWidth, ViewportHeight, newCenterX, newCenterY, newScale);
    }

    public Point2D WorldToScreen(Point2D world)
    {
        double x = ((world.X - WorldCenterX) * PixelsPerWorldUnit) + (ViewportWidth * 0.5d);
        double y = ((WorldCenterY - world.Y) * PixelsPerWorldUnit) + (ViewportHeight * 0.5d);
        return new Point2D(x, y);
    }

    public Point2D ScreenToWorld(Point2D screen)
    {
        double x = ((screen.X - (ViewportWidth * 0.5d)) / PixelsPerWorldUnit) + WorldCenterX;
        double y = WorldCenterY - ((screen.Y - (ViewportHeight * 0.5d)) / PixelsPerWorldUnit);
        return new Point2D(x, y);
    }

    private static double ClampScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale))
        {
            return 1d;
        }

        return Math.Max(MinPixelsPerWorldUnit, Math.Min(MaxPixelsPerWorldUnit, scale));
    }
}
