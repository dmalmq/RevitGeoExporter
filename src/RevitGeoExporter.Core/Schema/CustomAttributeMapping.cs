using System;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Schema;

public enum CustomAttributeSourceKind
{
    RevitParameter = 0,
    DerivedValue = 1,
    Constant = 2,
    SourceMetadata = 3,
}

public enum CustomAttributeNullBehavior
{
    PreserveNull = 0,
    EmptyString = 1,
    UseDefault = 2,
}

public sealed class CustomAttributeMapping
{
    public SchemaLayerType Layer { get; set; } = SchemaLayerType.Unit;

    public string FieldName { get; set; } = string.Empty;

    public CustomAttributeSourceKind SourceKind { get; set; } = CustomAttributeSourceKind.RevitParameter;

    // RevitParameter, DerivedValue, and SourceMetadata all resolve through SourceKey.
    public string SourceKey { get; set; } = string.Empty;

    public string ConstantValue { get; set; } = string.Empty;

    public ExportAttributeType TargetType { get; set; } = ExportAttributeType.Text;

    public CustomAttributeNullBehavior NullBehavior { get; set; } = CustomAttributeNullBehavior.PreserveNull;

    public string DefaultValue { get; set; } = string.Empty;

    public CustomAttributeMapping Clone()
    {
        return new CustomAttributeMapping
        {
            Layer = Layer,
            FieldName = Normalize(FieldName),
            SourceKind = SourceKind,
            SourceKey = Normalize(SourceKey),
            ConstantValue = Normalize(ConstantValue),
            TargetType = TargetType,
            NullBehavior = NullBehavior,
            DefaultValue = Normalize(DefaultValue),
        };
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
