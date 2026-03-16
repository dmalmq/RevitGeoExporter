using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Schema;

namespace RevitGeoExporter.Export;

internal static class SchemaAttributeMapper
{
    internal const string SchemaIssuesAttributeName = "_schema_issues";

    public static IReadOnlyList<AttributeDefinition> BuildAttributeDefinitions(
        SchemaProfile? schemaProfile,
        SchemaLayerType layerType,
        IEnumerable<string> reservedFieldNames,
        ICollection<string>? warnings = null)
    {
        HashSet<string> reserved = new(
            (reservedFieldNames ?? Array.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> seenCustomFields = new(StringComparer.OrdinalIgnoreCase);
        List<AttributeDefinition> definitions = new();

        foreach (CustomAttributeMapping mapping in GetLayerMappings(schemaProfile, layerType))
        {
            string fieldName = mapping.FieldName.Trim();
            if (fieldName.Length == 0)
            {
                continue;
            }

            if (reserved.Contains(fieldName))
            {
                warnings?.Add(
                    $"Schema field '{fieldName}' on layer '{layerType.ToString().ToLowerInvariant()}' matches an existing exported attribute and was ignored.");
                continue;
            }

            if (!seenCustomFields.Add(fieldName))
            {
                warnings?.Add(
                    $"Schema field '{fieldName}' appears more than once on layer '{layerType.ToString().ToLowerInvariant()}' and only the first mapping will be used.");
                continue;
            }

            definitions.Add(new AttributeDefinition(fieldName, mapping.TargetType));
        }

        return definitions;
    }

    public static void ApplyMappings(
        SchemaProfile? schemaProfile,
        SchemaLayerType layerType,
        IDictionary<string, object?> attributes,
        Element? sourceElement,
        string? viewName,
        ICollection<string> warnings)
    {
        if (attributes == null)
        {
            throw new ArgumentNullException(nameof(attributes));
        }

        if (warnings == null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        HashSet<string> reservedFieldNames = new(
            attributes.Keys.Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);
        List<SchemaAttributeIssue> issues = new();

        foreach (CustomAttributeMapping mapping in GetLayerMappings(schemaProfile, layerType))
        {
            string fieldName = mapping.FieldName.Trim();
            if (fieldName.Length == 0 || reservedFieldNames.Contains(fieldName))
            {
                continue;
            }

            if (!TryResolveSourceValue(mapping, attributes, sourceElement, layerType, viewName, out object? sourceValue, out string? sourceFailure))
            {
                object? fallbackValue = ResolveFallbackValue(mapping);
                string message = BuildMissingValueMessage(layerType, fieldName, sourceFailure, sourceElement, fallbackValue);
                AddIssue(issues, warnings, SchemaAttributeIssueCode.MissingMappedParameter, fieldName, message);
                if (!TryAssignCoercedValue(attributes, mapping, fieldName, fallbackValue, layerType, sourceElement, warnings, issues))
                {
                    attributes[fieldName] = null;
                }

                reservedFieldNames.Add(fieldName);
                continue;
            }

            if (!TryAssignCoercedValue(attributes, mapping, fieldName, sourceValue, layerType, sourceElement, warnings, issues))
            {
                object? fallbackValue = ResolveFallbackValue(mapping);
                if (!TryAssignCoercedValue(attributes, mapping, fieldName, fallbackValue, layerType, sourceElement, warnings, issues, sourceWasFallback: true))
                {
                    attributes[fieldName] = null;
                }
            }

            reservedFieldNames.Add(fieldName);
        }

        if (issues.Count > 0)
        {
            attributes[SchemaIssuesAttributeName] = issues;
        }
    }

    private static bool TryAssignCoercedValue(
        IDictionary<string, object?> attributes,
        CustomAttributeMapping mapping,
        string fieldName,
        object? rawValue,
        SchemaLayerType layerType,
        Element? sourceElement,
        ICollection<string> warnings,
        ICollection<SchemaAttributeIssue> issues,
        bool sourceWasFallback = false)
    {
        if (rawValue == null)
        {
            attributes[fieldName] = null;
            return true;
        }

        if (SchemaValueCoercion.TryCoerce(rawValue, mapping.TargetType, out object? coercedValue, out string? failureReason))
        {
            attributes[fieldName] = coercedValue;
            return true;
        }

        string message = BuildConversionFailureMessage(
            layerType,
            fieldName,
            rawValue,
            mapping.TargetType,
            failureReason,
            sourceElement,
            sourceWasFallback);
        AddIssue(issues, warnings, SchemaAttributeIssueCode.TypeConversionFailed, fieldName, message);
        return false;
    }

    private static object? ResolveFallbackValue(CustomAttributeMapping mapping)
    {
        switch (mapping.NullBehavior)
        {
            case CustomAttributeNullBehavior.EmptyString:
                return string.Empty;
            case CustomAttributeNullBehavior.UseDefault:
                return string.IsNullOrWhiteSpace(mapping.DefaultValue) ? null : mapping.DefaultValue.Trim();
            case CustomAttributeNullBehavior.PreserveNull:
            default:
                return null;
        }
    }

    private static bool TryResolveSourceValue(
        CustomAttributeMapping mapping,
        IDictionary<string, object?> attributes,
        Element? sourceElement,
        SchemaLayerType layerType,
        string? viewName,
        out object? value,
        out string? failure)
    {
        value = null;
        failure = null;

        switch (mapping.SourceKind)
        {
            case CustomAttributeSourceKind.Constant:
                value = mapping.ConstantValue;
                return true;
            case CustomAttributeSourceKind.RevitParameter:
                return TryReadRevitParameterValue(mapping, sourceElement, out value, out failure);
            case CustomAttributeSourceKind.DerivedValue:
                return TryReadDerivedValue(mapping, attributes, layerType, viewName, out value, out failure);
            case CustomAttributeSourceKind.SourceMetadata:
                return TryReadSourceMetadataValue(mapping, attributes, out value, out failure);
            default:
                failure = $"Schema source kind '{mapping.SourceKind}' is not supported.";
                return false;
        }
    }

    private static bool TryReadRevitParameterValue(
        CustomAttributeMapping mapping,
        Element? sourceElement,
        out object? value,
        out string? failure)
    {
        value = null;
        string parameterName = mapping.SourceKey?.Trim() ?? string.Empty;
        if (parameterName.Length == 0)
        {
            failure = "The mapped Revit parameter name is empty.";
            return false;
        }

        if (sourceElement == null)
        {
            failure = $"Revit parameter '{parameterName}' cannot be read because this feature has no source element.";
            return false;
        }

        Parameter? parameter = sourceElement.LookupParameter(parameterName);
        if (parameter == null)
        {
            failure = $"Revit parameter '{parameterName}' was not found on element {sourceElement.Id.Value}.";
            return false;
        }

        switch (parameter.StorageType)
        {
            case StorageType.String:
                value = NormalizeString(parameter.AsString());
                break;
            case StorageType.Integer:
                value = parameter.AsInteger();
                break;
            case StorageType.Double:
                value = mapping.TargetType == ExportAttributeType.Text
                    ? NormalizeString(parameter.AsValueString()) ?? (object)parameter.AsDouble()
                    : parameter.AsDouble();
                break;
            case StorageType.ElementId:
                value = parameter.AsElementId()?.Value;
                break;
            case StorageType.None:
                value = NormalizeString(parameter.AsValueString());
                break;
            default:
                value = null;
                break;
        }

        if (value == null)
        {
            failure = $"Revit parameter '{parameterName}' on element {sourceElement.Id.Value} has no value.";
            return false;
        }

        failure = null;
        return true;
    }

    private static bool TryReadDerivedValue(
        CustomAttributeMapping mapping,
        IDictionary<string, object?> attributes,
        SchemaLayerType layerType,
        string? viewName,
        out object? value,
        out string? failure)
    {
        value = null;
        if (!Enum.TryParse(mapping.SourceKey?.Trim(), ignoreCase: true, out SchemaDerivedValue derivedValue))
        {
            failure = $"Derived value '{mapping.SourceKey}' is not supported.";
            return false;
        }

        value = derivedValue switch
        {
            SchemaDerivedValue.ExportId => TryGetAttribute(attributes, "id"),
            SchemaDerivedValue.FeatureType => layerType.ToString().ToLowerInvariant(),
            SchemaDerivedValue.Category => TryGetAttribute(attributes, "category"),
            SchemaDerivedValue.Restrict => TryGetAttribute(attributes, "restrict"),
            SchemaDerivedValue.Name => TryGetAttribute(attributes, "name"),
            SchemaDerivedValue.AltName => TryGetAttribute(attributes, "alt_name"),
            SchemaDerivedValue.LevelId => TryGetAttribute(attributes, "level_id") ?? TryGetAttribute(attributes, "id"),
            SchemaDerivedValue.LevelName => TryGetAttribute(attributes, "level_name"),
            SchemaDerivedValue.ViewName => NormalizeString(viewName),
            SchemaDerivedValue.SourceElementId => TryGetAttribute(attributes, "source_element_id"),
            SchemaDerivedValue.ElementId => TryGetAttribute(attributes, "element_id"),
            SchemaDerivedValue.SourceLabel => TryGetAttribute(attributes, "source_label"),
            SchemaDerivedValue.DisplayPoint => TryGetAttribute(attributes, "display_point"),
            SchemaDerivedValue.Ordinal => TryGetAttribute(attributes, "ordinal"),
            SchemaDerivedValue.ElevationMeters => TryGetAttribute(attributes, "elevation_m"),
            _ => null,
        };

        if (value == null)
        {
            failure = $"Derived value '{derivedValue}' is not available for this feature.";
            return false;
        }

        failure = null;
        return true;
    }

    private static bool TryReadSourceMetadataValue(
        CustomAttributeMapping mapping,
        IDictionary<string, object?> attributes,
        out object? value,
        out string? failure)
    {
        value = null;
        if (!Enum.TryParse(mapping.SourceKey?.Trim(), ignoreCase: true, out SchemaSourceMetadataValue metadataValue))
        {
            failure = $"Source metadata value '{mapping.SourceKey}' is not supported.";
            return false;
        }

        value = metadataValue switch
        {
            SchemaSourceMetadataValue.SourceDocumentKey => TryGetAttribute(attributes, "source_document_key"),
            SchemaSourceMetadataValue.SourceDocumentName => TryGetAttribute(attributes, "source_document_name"),
            SchemaSourceMetadataValue.IsLinkedSource => TryGetAttribute(attributes, "is_linked_source"),
            SchemaSourceMetadataValue.SourceLinkInstanceId => TryGetAttribute(attributes, "source_link_instance_id"),
            SchemaSourceMetadataValue.SourceLinkInstanceName => TryGetAttribute(attributes, "source_link_instance_name"),
            SchemaSourceMetadataValue.HasPersistedExportId => TryGetAttribute(attributes, "has_persisted_export_id"),
            _ => null,
        };

        if (value == null)
        {
            failure = $"Source metadata value '{metadataValue}' is not available for this feature.";
            return false;
        }

        failure = null;
        return true;
    }

    private static IReadOnlyList<CustomAttributeMapping> GetLayerMappings(SchemaProfile? schemaProfile, SchemaLayerType layerType)
    {
        if (schemaProfile?.Mappings == null || schemaProfile.Mappings.Count == 0)
        {
            return Array.Empty<CustomAttributeMapping>();
        }

        return schemaProfile.Mappings
            .Where(mapping => mapping != null && mapping.Layer == layerType)
            .Select(mapping => mapping.Clone())
            .ToList();
    }

    private static object? TryGetAttribute(IDictionary<string, object?> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out object? value))
        {
            return null;
        }

        return value is string stringValue
            ? NormalizeString(stringValue)
            : value;
    }

    private static string? NormalizeString(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string BuildMissingValueMessage(
        SchemaLayerType layerType,
        string fieldName,
        string? sourceFailure,
        Element? sourceElement,
        object? fallbackValue)
    {
        string fallbackSuffix = fallbackValue == null
            ? " Null was written instead."
            : $" Fallback value '{FormatValue(fallbackValue)}' was used instead.";
        return $"Schema field '{fieldName}' on {layerType.ToString().ToLowerInvariant()} element {FormatElementId(sourceElement)} could not resolve its mapped source. {sourceFailure}{fallbackSuffix}";
    }

    private static string BuildConversionFailureMessage(
        SchemaLayerType layerType,
        string fieldName,
        object value,
        ExportAttributeType targetType,
        string? failureReason,
        Element? sourceElement,
        bool sourceWasFallback)
    {
        string valueSource = sourceWasFallback ? "fallback value" : "source value";
        return $"Schema field '{fieldName}' on {layerType.ToString().ToLowerInvariant()} element {FormatElementId(sourceElement)} could not convert {valueSource} '{FormatValue(value)}' to {targetType}. {failureReason}";
    }

    private static void AddIssue(
        ICollection<SchemaAttributeIssue> issues,
        ICollection<string> warnings,
        SchemaAttributeIssueCode code,
        string fieldName,
        string message)
    {
        issues.Add(new SchemaAttributeIssue(code, fieldName, message));
        warnings.Add(message);
    }

    private static string FormatElementId(Element? sourceElement)
    {
        return sourceElement?.Id.Value.ToString(CultureInfo.InvariantCulture) ?? "<none>";
    }

    private static string FormatValue(object value)
    {
        return value switch
        {
            bool boolValue => boolValue ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }
}
