using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Coordinates;

public sealed class AffineTransform2DTests
{
    private const double Tolerance = 0.000001d;

    [Fact]
    public void Transform_AppliesOriginAndBasisVectors()
    {
        AffineTransform2D transform = new(
            new Point2D(10d, 20d),
            new Point2D(2d, 3d),
            new Point2D(-4d, 5d));

        Point2D point = transform.Transform(6d, 7d);

        Assert.InRange(point.X, -6d - Tolerance, -6d + Tolerance);
        Assert.InRange(point.Y, 73d - Tolerance, 73d + Tolerance);
    }

    [Fact]
    public void TransformVector_DoesNotApplyOrigin()
    {
        AffineTransform2D transform = new(
            new Point2D(10d, 20d),
            new Point2D(2d, 3d),
            new Point2D(-4d, 5d));

        Point2D vector = transform.TransformVector(6d, 7d);

        Assert.InRange(vector.X, -16d - Tolerance, -16d + Tolerance);
        Assert.InRange(vector.Y, 53d - Tolerance, 53d + Tolerance);
    }
}
