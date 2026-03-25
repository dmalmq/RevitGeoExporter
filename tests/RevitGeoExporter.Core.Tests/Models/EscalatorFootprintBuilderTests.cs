using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Models;

public sealed class EscalatorFootprintBuilderTests
{
    private const double ComparisonTolerance = 1e-6d;

    [Fact]
    public void TryCreate_WhenUsingRotatedFootprintPointsWithoutBoundingBox_PreservesActualLengthAndWidth()
    {
        Point2D center = new(12d, -3d);
        Point2D axis = Normalize(0.9659258262890683d, 0.2588190451025207d);
        IReadOnlyList<Point2D> geometryPoints = CreateRectanglePoints(center, axis, length: 10d, width: 2d);

        bool created = EscalatorFootprintBuilder.TryCreate(
            center,
            axis,
            geometryPoints,
            explicitLengthMeters: null,
            explicitWidthMeters: null,
            boundingBoxPoints: null,
            minLengthMeters: 0.5d,
            minWidthMeters: 0.3d,
            out EscalatorFootprintProjection footprint);

        Assert.True(created);
        Assert.False(footprint.UsedBoundingBoxLength);
        Assert.False(footprint.UsedBoundingBoxWidth);
        AssertApproximatelyEqual(10d, footprint.LengthMeters);
        AssertApproximatelyEqual(2d, footprint.WidthMeters);
    }

    [Fact]
    public void TryCreate_WhenBoundingBoxExists_PrefersBoundingBoxLengthAndWidth()
    {
        Point2D center = new(3d, 7d);
        Point2D axis = Normalize(0.8660254037844386d, 0.5d);
        IReadOnlyList<Point2D> geometryPoints = CreateRectanglePoints(center, axis, length: 1.0d, width: 1.2d);
        IReadOnlyList<Point2D> boundingBoxPoints = CreateRectanglePoints(center, axis, length: 8.8d, width: 4.6d);

        bool created = EscalatorFootprintBuilder.TryCreate(
            center,
            axis,
            geometryPoints,
            explicitLengthMeters: 1.0d,
            explicitWidthMeters: 1.4d,
            boundingBoxPoints,
            minLengthMeters: 0.5d,
            minWidthMeters: 0.3d,
            out EscalatorFootprintProjection footprint);

        Assert.True(created);
        Assert.True(footprint.UsedBoundingBoxLength);
        Assert.True(footprint.UsedBoundingBoxWidth);
        AssertApproximatelyEqual(8.8d, footprint.LengthMeters);
        AssertApproximatelyEqual(4.6d, footprint.WidthMeters);
    }

    [Fact]
    public void TryCreate_WhenBoundingBoxIsUnavailable_FallsBackToExplicitLengthAndWidth()
    {
        Point2D center = new(0d, 0d);
        Point2D axis = Normalize(4d, 3d);

        bool created = EscalatorFootprintBuilder.TryCreate(
            center,
            axis,
            geometryPoints: Array.Empty<Point2D>(),
            explicitLengthMeters: 9d,
            explicitWidthMeters: 1.6d,
            boundingBoxPoints: null,
            minLengthMeters: 0.5d,
            minWidthMeters: 0.3d,
            out EscalatorFootprintProjection footprint);

        Assert.True(created);
        Assert.False(footprint.UsedBoundingBoxLength);
        Assert.False(footprint.UsedBoundingBoxWidth);
        AssertApproximatelyEqual(9d, footprint.LengthMeters);
        AssertApproximatelyEqual(1.6d, footprint.WidthMeters);
    }

    [Fact]
    public void ClampWidth_WhenBoundingBoxWidthExceedsLimit_CapsWidthAndPreservesLength()
    {
        EscalatorFootprintProjection footprint = new(
            center: new Point2D(5d, 2d),
            axis: Normalize(0.8660254037844386d, 0.5d),
            minAlong: -4.4d,
            maxAlong: 4.4d,
            minAcross: -2.3d,
            maxAcross: 2.3d,
            usedBoundingBoxLength: true,
            usedBoundingBoxWidth: true);

        EscalatorFootprintProjection clamped = footprint.ClampWidth(1.5d);

        Assert.True(clamped.UsedBoundingBoxLength);
        Assert.True(clamped.UsedBoundingBoxWidth);
        AssertApproximatelyEqual(8.8d, clamped.LengthMeters);
        AssertApproximatelyEqual(1.5d, clamped.WidthMeters);
        AssertApproximatelyEqual(0d, clamped.MinAcross + clamped.MaxAcross);
    }

    [Fact]
    public void ClampWidth_WhenWidthIsAlreadyBelowLimit_LeavesFootprintUnchanged()
    {
        EscalatorFootprintProjection footprint = new(
            center: new Point2D(5d, 2d),
            axis: Normalize(0.8660254037844386d, 0.5d),
            minAlong: -4.4d,
            maxAlong: 4.4d,
            minAcross: -0.6d,
            maxAcross: 0.6d,
            usedBoundingBoxLength: true,
            usedBoundingBoxWidth: true);

        EscalatorFootprintProjection clamped = footprint.ClampWidth(1.5d);

        AssertApproximatelyEqual(8.8d, clamped.LengthMeters);
        AssertApproximatelyEqual(1.2d, clamped.WidthMeters);
        AssertApproximatelyEqual(footprint.MinAcross, clamped.MinAcross);
        AssertApproximatelyEqual(footprint.MaxAcross, clamped.MaxAcross);
    }

    [Fact]
    public void OrientLongerSideAlongAxis_WhenWidthExceedsLength_SwapsTheAxesSoLengthIsLonger()
    {
        EscalatorFootprintProjection footprint = new(
            center: new Point2D(5d, 2d),
            axis: Normalize(1d, 0d),
            minAlong: -0.6d,
            maxAlong: 0.6d,
            minAcross: -4.4d,
            maxAcross: 4.4d,
            usedBoundingBoxLength: true,
            usedBoundingBoxWidth: true);

        EscalatorFootprintProjection oriented = footprint.OrientLongerSideAlongAxis();

        Assert.True(oriented.UsedBoundingBoxLength);
        Assert.True(oriented.UsedBoundingBoxWidth);
        AssertApproximatelyEqual(8.8d, oriented.LengthMeters);
        AssertApproximatelyEqual(1.2d, oriented.WidthMeters);
        AssertApproximatelyEqual(1d, oriented.Axis.Y);
    }

    private static IReadOnlyList<Point2D> CreateRectanglePoints(
        Point2D center,
        Point2D axis,
        double length,
        double width)
    {
        Point2D normalizedAxis = Normalize(axis.X, axis.Y);
        Point2D perpendicular = new(-normalizedAxis.Y, normalizedAxis.X);
        double halfLength = length * 0.5d;
        double halfWidth = width * 0.5d;

        Point2D p1 = ToWorldPoint(center, normalizedAxis, perpendicular, -halfLength, -halfWidth);
        Point2D p2 = ToWorldPoint(center, normalizedAxis, perpendicular, halfLength, -halfWidth);
        Point2D p3 = ToWorldPoint(center, normalizedAxis, perpendicular, halfLength, halfWidth);
        Point2D p4 = ToWorldPoint(center, normalizedAxis, perpendicular, -halfLength, halfWidth);
        return new[] { p1, p2, p3, p4, p1 };
    }

    private static Point2D ToWorldPoint(
        Point2D center,
        Point2D axis,
        Point2D perpendicular,
        double along,
        double across)
    {
        return new Point2D(
            center.X + (axis.X * along) + (perpendicular.X * across),
            center.Y + (axis.Y * along) + (perpendicular.Y * across));
    }

    private static Point2D Normalize(double x, double y)
    {
        double length = Math.Sqrt((x * x) + (y * y));
        return new Point2D(x / length, y / length);
    }

    private static void AssertApproximatelyEqual(double expected, double actual)
    {
        Assert.True(
            Math.Abs(expected - actual) <= ComparisonTolerance,
            $"Expected {expected} but found {actual}.");
    }
}
