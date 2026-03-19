using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Export;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Export;

public sealed class FloorExportDataPreparerNormalizationTests
{
    private const double ComparisonTolerance = 1e-6d;

    private static readonly MethodInfo NormalizeUnitFeaturesMethod =
        typeof(FloorExportDataPreparer).GetMethod(
            "NormalizeUnitFeatures",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("NormalizeUnitFeatures method was not found.");

    [Fact]
    public void NormalizeUnitFeatures_WhenRepairDisabled_KeepsNonVerticalBoundsUnchanged()
    {
        ExportPolygon left = CreateFeature("left", "nonpublic", 0d, 0d, 2d, 2d);
        ExportPolygon right = CreateFeature("right", "nonpublic", 2.10d, 0d, 4.10d, 2d);

        IReadOnlyList<ExportPolygon> normalized = Normalize(
            new[] { left, right },
            new GeometryRepairOptions
            {
                Enabled = false,
                MergeNearbyBoundaryThresholdMeters = 0.15d,
            }.GetEffectiveOptions());

        AssertBoundsEqual(GetBounds(left), GetBounds(FindById(normalized, "left")));
        AssertBoundsEqual(GetBounds(right), GetBounds(FindById(normalized, "right")));
    }

    [Fact]
    public void NormalizeUnitFeatures_WhenRepairEnabled_KeepsVerticalBoundsUnchanged()
    {
        ExportPolygon stair = CreateFeature("stair", "stairs", 0d, 0d, 2d, 2d);
        ExportPolygon escalator = CreateFeature("escalator", "escalator", 2.10d, 0d, 3.10d, 2d);

        IReadOnlyList<ExportPolygon> normalized = Normalize(
            new[] { stair, escalator },
            new GeometryRepairOptions
            {
                Enabled = true,
                MergeNearbyBoundaryThresholdMeters = 0.15d,
            }.GetEffectiveOptions());

        AssertBoundsEqual(GetBounds(stair), GetBounds(FindById(normalized, "stair")));
        AssertBoundsEqual(GetBounds(escalator), GetBounds(FindById(normalized, "escalator")));
    }

    [Fact]
    public void NormalizeUnitFeatures_WhenRepairEnabled_StillExpandsNonVerticalUnits()
    {
        ExportPolygon left = CreateFeature("left", "nonpublic", 0d, 0d, 2d, 2d);
        ExportPolygon right = CreateFeature("right", "nonpublic", 2.10d, 0d, 4.10d, 2d);

        IReadOnlyList<ExportPolygon> normalized = Normalize(
            new[] { left, right },
            new GeometryRepairOptions
            {
                Enabled = true,
                MergeNearbyBoundaryThresholdMeters = 0.15d,
            }.GetEffectiveOptions());

        ExportPolygon normalizedLeft = FindById(normalized, "left");
        ExportPolygon normalizedRight = FindById(normalized, "right");

        Assert.True(GetArea(normalizedLeft) > GetArea(left));
        Assert.True(GetArea(normalizedRight) > GetArea(right));
    }

    private static IReadOnlyList<ExportPolygon> Normalize(
        IReadOnlyList<ExportPolygon> features,
        GeometryRepairOptions options)
    {
        object? result = NormalizeUnitFeaturesMethod.Invoke(
            null,
            new object[]
            {
                features,
                options,
                new GeometryRepairResult(),
                new List<string>(),
            });

        return Assert.IsAssignableFrom<IReadOnlyList<ExportPolygon>>(result);
    }

    private static ExportPolygon FindById(IEnumerable<ExportPolygon> features, string id)
    {
        return Assert.Single(features, feature => string.Equals(ReadId(feature), id, StringComparison.Ordinal));
    }

    private static string ReadId(ExportPolygon feature)
    {
        return Assert.IsType<string>(feature.Attributes["id"]);
    }

    private static ExportPolygon CreateFeature(
        string id,
        string category,
        double minX,
        double minY,
        double maxX,
        double maxY)
    {
        Polygon2D polygon = new(new[]
        {
            new Point2D(minX, minY),
            new Point2D(maxX, minY),
            new Point2D(maxX, maxY),
            new Point2D(minX, maxY),
            new Point2D(minX, minY),
        });

        Dictionary<string, object?> attributes = new(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["category"] = category,
        };
        return new ExportPolygon(polygon, attributes);
    }

    private static Bounds GetBounds(ExportPolygon feature)
    {
        IEnumerable<Point2D> points = feature.Polygons.SelectMany(polygon => polygon.ExteriorRing);
        return new Bounds(
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Max(point => point.X),
            points.Max(point => point.Y));
    }

    private static double GetArea(ExportPolygon feature)
    {
        return feature.Polygons.Sum(GetArea);
    }

    private static double GetArea(Polygon2D polygon)
    {
        double area = 0d;
        IReadOnlyList<Point2D> ring = polygon.ExteriorRing;
        for (int i = 0; i < ring.Count - 1; i++)
        {
            area += (ring[i].X * ring[i + 1].Y) - (ring[i + 1].X * ring[i].Y);
        }

        return Math.Abs(area) * 0.5d;
    }

    private static void AssertBoundsEqual(Bounds expected, Bounds actual)
    {
        AssertApproximatelyEqual(expected.MinX, actual.MinX);
        AssertApproximatelyEqual(expected.MinY, actual.MinY);
        AssertApproximatelyEqual(expected.MaxX, actual.MaxX);
        AssertApproximatelyEqual(expected.MaxY, actual.MaxY);
    }

    private static void AssertApproximatelyEqual(double expected, double actual)
    {
        Assert.True(
            Math.Abs(expected - actual) <= ComparisonTolerance,
            $"Expected {expected} but found {actual}.");
    }

    private readonly struct Bounds
    {
        public Bounds(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public double MinX { get; }

        public double MinY { get; }

        public double MaxX { get; }

        public double MaxY { get; }
    }
}
