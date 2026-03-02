using RevitGeoExporter.Core.GeoPackage;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Export;

public static class LayerDefinition
{
    public static ExportLayer CreateUnitLayer()
    {
        return new ExportLayer(
            name: "unit",
            geometryType: GpkgGeometryType.MultiPolygon,
            attributes: new[]
            {
                new AttributeDefinition("id", ExportAttributeType.Text),
                new AttributeDefinition("category", ExportAttributeType.Text),
                new AttributeDefinition("restrict", ExportAttributeType.Text),
                new AttributeDefinition("name", ExportAttributeType.Text),
                new AttributeDefinition("alt_name", ExportAttributeType.Text),
                new AttributeDefinition("level_id", ExportAttributeType.Text),
                new AttributeDefinition("source", ExportAttributeType.Text),
                new AttributeDefinition("display_point", ExportAttributeType.Text),
            });
    }

    public static ExportLayer CreateDetailLayer()
    {
        return new ExportLayer(
            name: "detail",
            geometryType: GpkgGeometryType.LineString,
            attributes: new[]
            {
                new AttributeDefinition("id", ExportAttributeType.Text),
                new AttributeDefinition("level_id", ExportAttributeType.Text),
                new AttributeDefinition("element_id", ExportAttributeType.Integer),
            });
    }

    public static ExportLayer CreateOpeningLayer()
    {
        return new ExportLayer(
            name: "opening",
            geometryType: GpkgGeometryType.LineString,
            attributes: new[]
            {
                new AttributeDefinition("id", ExportAttributeType.Text),
                new AttributeDefinition("category", ExportAttributeType.Text),
                new AttributeDefinition("level_id", ExportAttributeType.Text),
                new AttributeDefinition("element_id", ExportAttributeType.Integer),
            });
    }

    public static ExportLayer CreateLevelLayer()
    {
        return new ExportLayer(
            name: "level",
            geometryType: GpkgGeometryType.MultiPolygon,
            attributes: new[]
            {
                new AttributeDefinition("id", ExportAttributeType.Text),
                new AttributeDefinition("level_name", ExportAttributeType.Text),
                new AttributeDefinition("ordinal", ExportAttributeType.Integer),
                new AttributeDefinition("elevation_m", ExportAttributeType.Real),
            });
    }
}
