using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitGeoExporter.Export;

public interface IExportMetadataProvider
{
    string GetElementId(Element element, ICollection<string> warnings);

    string GetLevelId(Level level, ICollection<string> warnings);

    string? GetOptionalStringParameter(Element element, string parameterName);
}
