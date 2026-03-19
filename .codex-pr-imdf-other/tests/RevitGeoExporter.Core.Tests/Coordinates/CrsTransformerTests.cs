using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Coordinates;

public sealed class CrsTransformerTests
{
    private const double Tolerance = 0.000001d;

    [Fact]
    public void FeetToMeters_ConvertsCorrectly()
    {
        CrsTransformer transformer = new();

        Point2D point = transformer.TransformFromRevitFeet(
            xFeet: 10d,
            yFeet: 20d,
            offsetXMeters: 0d,
            offsetYMeters: 0d,
            rotationDegrees: 0d);

        Assert.InRange(point.X, 3.048d - Tolerance, 3.048d + Tolerance);
        Assert.InRange(point.Y, 6.096d - Tolerance, 6.096d + Tolerance);
    }

    [Fact]
    public void OffsetAndRotation_AppliesExpectedTransform()
    {
        CrsTransformer transformer = new();
        Point2D inputMeters = new(1d, 0d);

        Point2D transformed = transformer.ApplyOffsetAndRotation(
            inputMeters,
            offsetXMeters: 10d,
            offsetYMeters: 20d,
            rotationDegrees: 90d);

        Assert.InRange(transformed.X, 10d - Tolerance, 10d + Tolerance);
        Assert.InRange(transformed.Y, 21d - Tolerance, 21d + Tolerance);
    }
}
