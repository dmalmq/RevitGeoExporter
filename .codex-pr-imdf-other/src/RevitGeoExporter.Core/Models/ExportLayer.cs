using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.GeoPackage;

namespace RevitGeoExporter.Core.Models;

public sealed class ExportLayer
{
    private readonly List<IExportFeature> _features = new();

    public ExportLayer(
        string name,
        GpkgGeometryType geometryType,
        IReadOnlyList<AttributeDefinition> attributes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Layer name is required.", nameof(name));
        }

        Name = name;
        GeometryType = geometryType;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public string Name { get; }

    public GpkgGeometryType GeometryType { get; }

    public IReadOnlyList<AttributeDefinition> Attributes { get; }

    public IReadOnlyList<IExportFeature> Features => _features;

    public void AddFeature(IExportFeature feature)
    {
        if (feature is null)
        {
            throw new ArgumentNullException(nameof(feature));
        }

        _features.Add(feature);
    }
}
