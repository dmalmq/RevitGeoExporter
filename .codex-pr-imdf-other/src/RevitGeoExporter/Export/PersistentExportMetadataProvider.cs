using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Utilities;

namespace RevitGeoExporter.Export;

public sealed class PersistentExportMetadataProvider : IExportMetadataProvider
{
    private readonly SharedParameterManager _parameterManager;
    private readonly string _hostDocumentKey;
    private readonly string _hostDocumentName;

    public PersistentExportMetadataProvider(SharedParameterManager parameterManager)
    {
        _parameterManager = parameterManager ?? throw new ArgumentNullException(nameof(parameterManager));
        _hostDocumentKey = DocumentProjectKeyBuilder.Create(_parameterManager.Document);
        _hostDocumentName = DocumentProjectKeyBuilder.CreateDisplayName(_parameterManager.Document);
    }

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

        if (ReferenceEquals(element.Document, _parameterManager.Document))
        {
            string id = _parameterManager.GetOrCreateElementId(element, warnings);
            bool hasPersistedId = string.Equals(
                _parameterManager.GetOptionalStringParameter(element, SharedParameterManager.ImdfIdParameterName),
                id,
                StringComparison.Ordinal);
            return new ExportElementMetadata(id, _hostDocumentKey, _hostDocumentName, hasPersistedId);
        }

        string sourceDocumentKey = DocumentProjectKeyBuilder.Create(element.Document);
        string sourceDocumentName = DocumentProjectKeyBuilder.CreateDisplayName(element.Document);
        string? existing = ReadOptionalStringParameter(element, SharedParameterManager.ImdfIdParameterName);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return new ExportElementMetadata(existing!, sourceDocumentKey, sourceDocumentName, hasPersistedId: true);
        }

        string fallbackId = DeterministicIdGenerator.CreateGuid(
            "linked-element",
            sourceDocumentKey,
            element.Id.Value.ToString(CultureInfo.InvariantCulture));
        warnings.Add(
            $"Linked element {element.Id.Value} in '{sourceDocumentName}' is missing '{SharedParameterManager.ImdfIdParameterName}'. Using deterministic fallback ID '{fallbackId}'.");
        return new ExportElementMetadata(fallbackId, sourceDocumentKey, sourceDocumentName, hasPersistedId: false);
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

        string id = _parameterManager.GetOrCreateLevelId(level, warnings);
        bool hasPersistedId = string.Equals(
            _parameterManager.GetOptionalStringParameter(level, SharedParameterManager.ImdfLevelIdParameterName),
            id,
            StringComparison.Ordinal);
        return new ExportLevelMetadata(id, hasPersistedId);
    }

    public string? GetOptionalStringParameter(Element element, string parameterName)
    {
        return ReadOptionalStringParameter(element, parameterName);
    }

    private string? ReadOptionalStringParameter(Element element, string parameterName)
    {
        if (ReferenceEquals(element.Document, _parameterManager.Document))
        {
            return _parameterManager.GetOptionalStringParameter(element, parameterName);
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
