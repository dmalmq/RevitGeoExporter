using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Diagnostics;
using RevitGeoExporter.Core.GeoPackage;
using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Diagnostics;

public sealed class ExportFingerprintBuilderTests
{
    [Fact]
    public void ComputeLayerFingerprint_IsDeterministicAcrossFeatureOrder()
    {
        ExportLayer first = CreateUnitLayer();
        first.AddFeature(CreateFeature("a", 0));
        first.AddFeature(CreateFeature("b", 10));

        ExportLayer second = CreateUnitLayer();
        second.AddFeature(CreateFeature("b", 10));
        second.AddFeature(CreateFeature("a", 0));

        ExportFingerprintBuilder builder = new();

        string firstFingerprint = builder.ComputeLayerFingerprint(new[] { first });
        string secondFingerprint = builder.ComputeLayerFingerprint(new[] { second });

        Assert.Equal(firstFingerprint, secondFingerprint);
    }

    [Fact]
    public void ComputeConfigurationFingerprint_ChangesWhenInputsChange()
    {
        ExportFingerprintBuilder builder = new();

        string baseline = builder.ComputeConfigurationFingerprint(new[] { "feature:unit", "crs:6677" });
        string changed = builder.ComputeConfigurationFingerprint(new[] { "feature:unit", "crs:6669" });

        Assert.NotEqual(baseline, changed);
    }

    private static ExportLayer CreateUnitLayer()
    {
        return new ExportLayer(
            "unit",
            GpkgGeometryType.MultiPolygon,
            new[]
            {
                new AttributeDefinition("id", ExportAttributeType.Text),
                new AttributeDefinition("category", ExportAttributeType.Text),
            });
    }

    private static ExportPolygon CreateFeature(string id, double originX)
    {
        return new ExportPolygon(
            new Polygon2D(
                new[]
                {
                    new Point2D(originX, 0),
                    new Point2D(originX + 5, 0),
                    new Point2D(originX + 5, 5),
                    new Point2D(originX, 5),
                }),
            new Dictionary<string, object?>
            {
                ["id"] = id,
                ["category"] = "walkway",
            });
    }
}
