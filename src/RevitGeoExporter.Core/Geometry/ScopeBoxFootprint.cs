using System;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Geometry;

public readonly struct ScopeBoxFootprint
{
    public ScopeBoxFootprint(
        Point2D origin,
        Point2D xBasis,
        Point2D yBasis,
        double minX,
        double minY,
        double maxX,
        double maxY)
    {
        Origin = origin;
        XBasis = xBasis;
        YBasis = yBasis;
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public Point2D Origin { get; }
    public Point2D XBasis { get; }
    public Point2D YBasis { get; }
    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
    public double Width => MaxX - MinX;
    public double Depth => MaxY - MinY;
    public double RotationDegrees => Math.Atan2(XBasis.Y, XBasis.X) * 180d / Math.PI;
}
