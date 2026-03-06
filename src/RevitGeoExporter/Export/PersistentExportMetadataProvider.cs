using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core;

namespace RevitGeoExporter.Export;

public sealed class PersistentExportMetadataProvider : IExportMetadataProvider
{
    private readonly SharedParameterManager _parameterManager;

    public PersistentExportMetadataProvider(SharedParameterManager parameterManager)
    {
        _parameterManager = parameterManager ?? throw new ArgumentNullException(nameof(parameterManager));
    }

    public string GetElementId(Element element, ICollection<string> warnings)
    {
        return _parameterManager.GetOrCreateElementId(element, warnings);
    }

    public string GetLevelId(Level level, ICollection<string> warnings)
    {
        return _parameterManager.GetOrCreateLevelId(level, warnings);
    }

    public string? GetOptionalStringParameter(Element element, string parameterName)
    {
        return _parameterManager.GetOptionalStringParameter(element, parameterName);
    }
}
