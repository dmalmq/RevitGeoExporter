using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Utilities;

namespace RevitGeoExporter.Export;

public sealed class PreviewExportMetadataProvider : IExportMetadataProvider
{
    public ExportElementMetadata GetElementMetadata(Element element, ICollection<string> warnings)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        string sourceDocumentKey = DocumentProjectKeyBuilder.Create(element.Document);
        string sourceDocumentName = DocumentProjectKeyBuilder.CreateDisplayName(element.Document);
        string? existing = GetOptionalStringParameter(element, SharedParameterManager.ImdfIdParameterName);
        if (existing != null)
        {
            string trimmedExisting = existing.Trim();
            if (trimmedExisting.Length > 0)
            {
                return new ExportElementMetadata(trimmedExisting, sourceDocumentKey, sourceDocumentName, hasPersistedId: true);
            }
        }

        string generated = DeterministicIdGenerator.CreateGuid(
            "preview-element",
            sourceDocumentKey,
            element.Id.Value.ToString(CultureInfo.InvariantCulture));
        warnings.Add(
            $"Element {element.Id.Value} in '{sourceDocumentName}' is missing '{SharedParameterManager.ImdfIdParameterName}'. Using transient preview ID '{generated}'.");
        return new ExportElementMetadata(generated, sourceDocumentKey, sourceDocumentName, hasPersistedId: false);
    }

    public ExportLevelMetadata GetLevelMetadata(Level level, ICollection<string> warnings)
    {
        if (level is null)
        {
            throw new ArgumentNullException(nameof(level));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        string? existing = GetOptionalStringParameter(level, SharedParameterManager.ImdfLevelIdParameterName);
        if (existing != null)
        {
            string trimmedExisting = existing.Trim();
            if (trimmedExisting.Length > 0)
            {
                return new ExportLevelMetadata(trimmedExisting, hasPersistedId: true);
            }
        }

        string generated = DeterministicIdGenerator.CreateGuid(
            "preview-level",
            level.Id.Value.ToString(CultureInfo.InvariantCulture));
        warnings.Add(
            $"Level {level.Id.Value} is missing '{SharedParameterManager.ImdfLevelIdParameterName}'. Using transient preview ID '{generated}'.");
        return new ExportLevelMetadata(generated, hasPersistedId: false);
    }

    public string? GetOptionalStringParameter(Element element, string parameterName)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (string.IsNullOrWhiteSpace(parameterName))
        {
            throw new ArgumentException("Parameter name is required.", nameof(parameterName));
        }

        Parameter? parameter = element.LookupParameter(parameterName);
        if (parameter == null || parameter.StorageType != StorageType.String)
        {
            return null;
        }

        string? value = parameter.AsString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
