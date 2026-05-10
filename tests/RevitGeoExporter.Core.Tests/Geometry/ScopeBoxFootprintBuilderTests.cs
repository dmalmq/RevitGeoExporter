using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Geometry;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Geometry;

public sealed class ScopeBoxFootprintBuilderTests
{
    [Fact]
    public void TryBuild_RotatedBox_PreservesRotationAndExtents()
    {
        const double angle = Math.PI / 6d;
        IReadOnlyList<ScopeBoxEdge3D> edges = CreateBoxEdges(
            centerX: 100d,
            centerY: -50d,
            zMin: 2d,
            zMax: 14d,
            width: 20d,
            depth: 10d,
            angleRadians: angle);

        bool result = ScopeBoxFootprintBuilder.TryBuild(edges, 1d, out ScopeBoxFootprint footprint);

        Assert.True(result);
        AssertClose(100d, footprint.Origin.X);
        AssertClose(-50d, footprint.Origin.Y);
        AssertClose(20d, footprint.Width);
        AssertClose(10d, footprint.Depth);

        double expectedX = Math.Cos(angle);
        double expectedY = Math.Sin(angle);
        double alignment = (footprint.XBasis.X * expectedX) + (footprint.XBasis.Y * expectedY);
        Assert.True(Math.Abs(alignment) > 0.999999d);

        double dot = (footprint.XBasis.X * footprint.YBasis.X) + (footprint.XBasis.Y * footprint.YBasis.Y);
        double determinant = (footprint.XBasis.X * footprint.YBasis.Y) - (footprint.XBasis.Y * footprint.YBasis.X);
        AssertClose(0d, dot);
        AssertClose(1d, determinant);
    }

    [Fact]
    public void TryBuild_AxisAlignedBox_UsesAxisAlignedBasis()
    {
        IReadOnlyList<ScopeBoxEdge3D> edges = CreateBoxEdges(
            centerX: 0d,
            centerY: 0d,
            zMin: 0d,
            zMax: 8d,
            width: 12d,
            depth: 6d,
            angleRadians: 0d);

        bool result = ScopeBoxFootprintBuilder.TryBuild(edges, 1d, out ScopeBoxFootprint footprint);

        Assert.True(result);
        AssertClose(1d, footprint.XBasis.X);
        AssertClose(0d, footprint.XBasis.Y);
        AssertClose(0d, footprint.YBasis.X);
        AssertClose(1d, footprint.YBasis.Y);
        AssertClose(12d, footprint.Width);
        AssertClose(6d, footprint.Depth);
    }

    [Fact]
    public void TryBuild_DegenerateEdges_ReturnsFalse()
    {
        var edges = new[]
        {
            new ScopeBoxEdge3D(0d, 0d, 0d, 0d, 0d, 10d),
            new ScopeBoxEdge3D(1d, 0d, 0d, 1d, 0d, 10d),
            new ScopeBoxEdge3D(0d, 1d, 0d, 0d, 1d, 10d),
            new ScopeBoxEdge3D(1d, 1d, 0d, 1d, 1d, 10d),
        };

        bool result = ScopeBoxFootprintBuilder.TryBuild(edges, 1d, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryBuild_TooSmallFootprint_ReturnsFalse()
    {
        IReadOnlyList<ScopeBoxEdge3D> edges = CreateBoxEdges(
            centerX: 0d,
            centerY: 0d,
            zMin: 0d,
            zMax: 8d,
            width: 0.5d,
            depth: 0.5d,
            angleRadians: Math.PI / 4d);

        bool result = ScopeBoxFootprintBuilder.TryBuild(edges, 1d, out _);

        Assert.False(result);
    }

    private static IReadOnlyList<ScopeBoxEdge3D> CreateBoxEdges(
        double centerX,
        double centerY,
        double zMin,
        double zMax,
        double width,
        double depth,
        double angleRadians)
    {
        double cos = Math.Cos(angleRadians);
        double sin = Math.Sin(angleRadians);
        Point3 xBasis = new(cos, sin, 0d);
        Point3 yBasis = new(-sin, cos, 0d);

        Point3 p00 = CreatePoint(centerX, centerY, zMin, xBasis, yBasis, -width / 2d, -depth / 2d);
        Point3 p10 = CreatePoint(centerX, centerY, zMin, xBasis, yBasis, width / 2d, -depth / 2d);
        Point3 p11 = CreatePoint(centerX, centerY, zMin, xBasis, yBasis, width / 2d, depth / 2d);
        Point3 p01 = CreatePoint(centerX, centerY, zMin, xBasis, yBasis, -width / 2d, depth / 2d);
        Point3 p00Top = p00.WithZ(zMax);
        Point3 p10Top = p10.WithZ(zMax);
        Point3 p11Top = p11.WithZ(zMax);
        Point3 p01Top = p01.WithZ(zMax);

        return new[]
        {
            Edge(p00, p10),
            Edge(p10, p11),
            Edge(p11, p01),
            Edge(p01, p00),
            Edge(p00Top, p10Top),
            Edge(p10Top, p11Top),
            Edge(p11Top, p01Top),
            Edge(p01Top, p00Top),
            Edge(p00, p00Top),
            Edge(p10, p10Top),
            Edge(p11, p11Top),
            Edge(p01, p01Top),
        };
    }

    private static Point3 CreatePoint(
        double centerX,
        double centerY,
        double z,
        Point3 xBasis,
        Point3 yBasis,
        double localX,
        double localY)
    {
        return new Point3(
            centerX + (xBasis.X * localX) + (yBasis.X * localY),
            centerY + (xBasis.Y * localX) + (yBasis.Y * localY),
            z);
    }

    private static ScopeBoxEdge3D Edge(Point3 start, Point3 end)
    {
        return new ScopeBoxEdge3D(start.X, start.Y, start.Z, end.X, end.Y, end.Z);
    }

    private static void AssertClose(double expected, double actual)
    {
        Assert.Equal(expected, actual, precision: 6);
    }

    private readonly struct Point3
    {
        public Point3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public Point3 WithZ(double z)
        {
            return new Point3(X, Y, z);
        }
    }
}
