using System.Collections.Generic;
using RevitGeoExporter.Core.Models;
using Xunit;

namespace RevitGeoExporter.Core.Tests.Models;

public sealed class UnitFeatureComposerTests
{
    [Fact]
    public void Compose_UsesRoomAttributesForHybridFloorGeometryWhenSpatialMatchExists()
    {
        ExportPolygon floorFeature = CreateFeature(
            minX: 0,
            minY: 0,
            maxX: 10,
            maxY: 10,
            id: "floor-1",
            sourceElementId: 1001,
            category: "floor-category",
            name: "Floor Unit",
            extraAttributes: new Dictionary<string, object?>
            {
                ["custom_code"] = "F-01",
            });
        ExportPolygon roomFeature = CreateFeature(
            minX: 1,
            minY: 1,
            maxX: 9,
            maxY: 9,
            id: "room-1",
            sourceElementId: 2001,
            category: "room-category",
            name: "Room Unit",
            extraAttributes: new Dictionary<string, object?>
            {
                ["custom_code"] = "R-01",
            });

        IReadOnlyList<ExportPolygon> composed = UnitFeatureComposer.Compose(
            new[] { floorFeature },
            new[] { roomFeature },
            UnitGeometrySource.Floors,
            UnitAttributeSource.Hybrid);

        ExportPolygon result = Assert.Single(composed);
        Assert.Equal("floor-1", result.Attributes["id"]);
        Assert.Equal(1001L, result.Attributes["source_element_id"]);
        Assert.Equal("room-category", result.Attributes["category"]);
        Assert.Equal("Room Unit", result.Attributes["name"]);
        Assert.Equal("R-01", result.Attributes["custom_code"]);
        Assert.Equal("floor", result.Attributes["unit_geometry_source_kind"]);
        Assert.Equal("room", result.Attributes["unit_attribute_source_kind"]);
    }

    [Fact]
    public void Compose_FallsBackToFloorAttributesWhenNoRoomMatchExists()
    {
        ExportPolygon floorFeature = CreateFeature(
            minX: 0,
            minY: 0,
            maxX: 10,
            maxY: 10,
            id: "floor-1",
            sourceElementId: 1001,
            category: "floor-category",
            name: "Floor Unit",
            extraAttributes: new Dictionary<string, object?>
            {
                ["custom_code"] = "F-01",
            });
        ExportPolygon roomFeature = CreateFeature(
            minX: 20,
            minY: 20,
            maxX: 30,
            maxY: 30,
            id: "room-1",
            sourceElementId: 2001,
            category: "room-category",
            name: "Room Unit");

        IReadOnlyList<ExportPolygon> composed = UnitFeatureComposer.Compose(
            new[] { floorFeature },
            new[] { roomFeature },
            UnitGeometrySource.Floors,
            UnitAttributeSource.Hybrid);

        ExportPolygon result = Assert.Single(composed);
        Assert.Equal("floor-category", result.Attributes["category"]);
        Assert.Equal("Floor Unit", result.Attributes["name"]);
        Assert.Equal("F-01", result.Attributes["custom_code"]);
        Assert.Equal("floor", result.Attributes["unit_geometry_source_kind"]);
        Assert.Equal("floor", result.Attributes["unit_attribute_source_kind"]);
    }

    private static ExportPolygon CreateFeature(
        double minX,
        double minY,
        double maxX,
        double maxY,
        string id,
        long sourceElementId,
        string category,
        string name,
        IReadOnlyDictionary<string, object?>? extraAttributes = null)
    {
        Dictionary<string, object?> attributes = new()
        {
            ["id"] = id,
            ["source_element_id"] = sourceElementId,
            ["category"] = category,
            ["name"] = name,
            ["level_id"] = "level-1",
            ["source"] = "TestModel",
            ["display_point"] = $"POINT ({(minX + maxX) / 2d} {(minY + maxY) / 2d})",
        };

        foreach (KeyValuePair<string, object?> entry in extraAttributes ?? new Dictionary<string, object?>())
        {
            attributes[entry.Key] = entry.Value;
        }

        return new ExportPolygon(
            new Polygon2D(
                new[]
                {
                    new Point2D(minX, minY),
                    new Point2D(maxX, minY),
                    new Point2D(maxX, maxY),
                    new Point2D(minX, maxY),
                }),
            attributes);
    }
}
