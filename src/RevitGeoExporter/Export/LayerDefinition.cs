using System.Collections.Generic;
using System.Linq;
using RevitGeoExporter.Core.GeoPackage;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Schema;

namespace RevitGeoExporter.Export;

public static class LayerDefinition
{
    public static ExportLayer CreateUnitLayer(SchemaProfile? schemaProfile = null, System.Collections.Generic.ICollection<string>? warnings = null)
    {
        return CreateLayer(
            "unit",
            GpkgGeometryType.MultiPolygon,
            SchemaLayerType.Unit,
            new[]
            {
                new AttributeDefinition("id", ExportAttributeType.Text),
                new AttributeDefinition("category", ExportAttributeType.Text),
                new AttributeDefinition("restrict", ExportAttributeType.Text),
                new AttributeDefinition("name", ExportAttributeType.Text),
                new AttributeDefinition("alt_name", ExportAttributeType.Text),
                new AttributeDefinition("level_id", ExportAttributeType.Text),
                new AttributeDefinition("source", ExportAttributeType.Text),
                new AttributeDefinition("display_point", ExportAttributeType.Text),
                new AttributeDefinition("preview_fill_color", ExportAttributeType.Text),
            },
            schemaProfile,
            warnings);
    }

    public static ExportLayer CreateDetailLayer(SchemaProfile? schemaProfile = null, System.Collections.Generic.ICollection<string>? warnings = null)
    {
        return CreateLayer(
            "detail",
            GpkgGeometryType.LineString,
            SchemaLayerType.Detail,
            new[]
            {
                new AttributeDefinition("id", ExportAttributeType.Text),
                new AttributeDefinition("level_id", ExportAttributeType.Text),
                new AttributeDefinition("element_id", ExportAttributeType.Integer),
            },
            schemaProfile,
            warnings);
    }

    public static ExportLayer CreateOpeningLayer(SchemaProfile? schemaProfile = null, System.Collections.Generic.ICollection<string>? warnings = null)
    {
        return CreateLayer(
            "opening",
            GpkgGeometryType.LineString,
            SchemaLayerType.Opening,
            new[]
            {
                new AttributeDefinition("id", ExportAttributeType.Text),
                new AttributeDefinition("category", ExportAttributeType.Text),
                new AttributeDefinition("level_id", ExportAttributeType.Text),
                new AttributeDefinition("element_id", ExportAttributeType.Integer),
            },
            schemaProfile,
            warnings);
    }

    public static ExportLayer CreateFixtureLayer(SchemaProfile? schemaProfile = null, System.Collections.Generic.ICollection<string>? warnings = null)
    {
        return CreateLayer(
            "fixture",
            GpkgGeometryType.MultiPolygon,
            SchemaLayerType.Fixture,
            new[]
            {
                new AttributeDefinition("id", ExportAttributeType.Text),
                new AttributeDefinition("type", ExportAttributeType.Text),
                new AttributeDefinition("name", ExportAttributeType.Text),
                new AttributeDefinition("alt_name", ExportAttributeType.Text),
                new AttributeDefinition("level_id", ExportAttributeType.Text),
                new AttributeDefinition("source", ExportAttributeType.Text),
                new AttributeDefinition("display_point", ExportAttributeType.Text),
            },
            schemaProfile,
            warnings);
    }

    public static ExportLayer CreateLevelLayer(SchemaProfile? schemaProfile = null, System.Collections.Generic.ICollection<string>? warnings = null)
    {
        return CreateLayer(
            "level",
            GpkgGeometryType.MultiPolygon,
            SchemaLayerType.Level,
            new[]
            {
                new AttributeDefinition("id", ExportAttributeType.Text),
                new AttributeDefinition("level_name", ExportAttributeType.Text),
                new AttributeDefinition("ordinal", ExportAttributeType.Integer),
                new AttributeDefinition("elevation_m", ExportAttributeType.Real),
            },
            schemaProfile,
            warnings);
    }

    private static ExportLayer CreateLayer(
        string name,
        GpkgGeometryType geometryType,
        SchemaLayerType layerType,
        IReadOnlyList<AttributeDefinition> coreAttributes,
        SchemaProfile? schemaProfile,
        System.Collections.Generic.ICollection<string>? warnings)
    {
        System.Collections.Generic.List<AttributeDefinition> attributes = new(coreAttributes);
        attributes.AddRange(SchemaAttributeMapper.BuildAttributeDefinitions(
            schemaProfile,
            layerType,
            coreAttributes.Select(attribute => attribute.Name),
            warnings));
        return new ExportLayer(name, geometryType, attributes);
    }
}
