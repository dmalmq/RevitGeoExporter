using System;

namespace RevitGeoExporter.Core.Models;

public sealed class AttributeDefinition
{
    public AttributeDefinition(string name, ExportAttributeType type)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Attribute name is required.", nameof(name));
        }

        Name = name;
        Type = type;
    }

    public string Name { get; }

    public ExportAttributeType Type { get; }
}

public enum ExportAttributeType
{
    Integer = 0,
    Real = 1,
    Text = 2,
    Boolean = 3,
}
