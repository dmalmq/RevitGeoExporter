using System;

namespace RevitGeoExporter.Core.Schema;

public enum SchemaAttributeIssueCode
{
    MissingMappedParameter = 0,
    TypeConversionFailed = 1,
    DuplicateFieldName = 2,
}

public sealed class SchemaAttributeIssue
{
    public SchemaAttributeIssue(
        SchemaAttributeIssueCode code,
        string fieldName,
        string message)
    {
        Code = code;
        FieldName = string.IsNullOrWhiteSpace(fieldName)
            ? throw new ArgumentException("A field name is required.", nameof(fieldName))
            : fieldName.Trim();
        Message = string.IsNullOrWhiteSpace(message)
            ? throw new ArgumentException("A message is required.", nameof(message))
            : message.Trim();
    }

    public SchemaAttributeIssueCode Code { get; }

    public string FieldName { get; }

    public string Message { get; }
}
