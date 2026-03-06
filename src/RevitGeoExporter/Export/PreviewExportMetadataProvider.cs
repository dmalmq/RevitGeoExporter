using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Utilities;

namespace RevitGeoExporter.Export;

public sealed class PreviewExportMetadataProvider : IExportMetadataProvider
{
    public string GetElementId(Element element, ICollection<string> warnings)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        string? existing = GetOptionalStringParameter(element, SharedParameterManager.ImdfIdParameterName);
        if (existing != null)
        {
            string trimmedExisting = existing.Trim();
            if (trimmedExisting.Length > 0)
            {
                return trimmedExisting;
            }
        }

        string generated = DeterministicIdGenerator.CreateGuid(
            "preview-element",
            element.Id.Value.ToString(CultureInfo.InvariantCulture));
        warnings.Add(
            $"Element {element.Id.Value} is missing '{SharedParameterManager.ImdfIdParameterName}'. Using transient preview ID '{generated}'.");
        return generated;
    }

    public string GetLevelId(Level level, ICollection<string> warnings)
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
                return trimmedExisting;
            }
        }

        string generated = DeterministicIdGenerator.CreateGuid(
            "preview-level",
            level.Id.Value.ToString(CultureInfo.InvariantCulture));
        warnings.Add(
            $"Level {level.Id.Value} is missing '{SharedParameterManager.ImdfLevelIdParameterName}'. Using transient preview ID '{generated}'.");
        return generated;
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
