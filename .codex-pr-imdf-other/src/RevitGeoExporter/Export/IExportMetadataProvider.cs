using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitGeoExporter.Export;

public interface IExportMetadataProvider
{
    ExportElementMetadata GetElementMetadata(Element element, ICollection<string> warnings);

    ExportLevelMetadata GetLevelMetadata(Level level, ICollection<string> warnings);

    string? GetOptionalStringParameter(Element element, string parameterName);
}
