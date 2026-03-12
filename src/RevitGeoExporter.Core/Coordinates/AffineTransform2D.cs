using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Coordinates;

public readonly struct AffineTransform2D
{
    public AffineTransform2D(Point2D origin, Point2D xBasis, Point2D yBasis)
    {
        Origin = origin;
        XBasis = xBasis;
        YBasis = yBasis;
    }

    public Point2D Origin { get; }

    public Point2D XBasis { get; }

    public Point2D YBasis { get; }

    public Point2D Transform(double x, double y)
    {
        return new Point2D(
            Origin.X + (XBasis.X * x) + (YBasis.X * y),
            Origin.Y + (XBasis.Y * x) + (YBasis.Y * y));
    }

    public Point2D Transform(Point2D point)
    {
        return Transform(point.X, point.Y);
    }

    public Point2D TransformVector(double x, double y)
    {
        return new Point2D(
            (XBasis.X * x) + (YBasis.X * y),
            (XBasis.Y * x) + (YBasis.Y * y));
    }

    public Point2D TransformVector(Point2D vector)
    {
        return TransformVector(vector.X, vector.Y);
    }
}
