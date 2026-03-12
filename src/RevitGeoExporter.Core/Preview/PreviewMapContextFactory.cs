using ProjNet.CoordinateSystems;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Preview;

public static class PreviewMapContextFactory
{
    public static PreviewMapContext Create(
        CoordinateExportMode coordinateMode,
        int targetEpsg,
        int? sourceEpsg,
        string? sourceCoordinateSystemId,
        string? sourceCoordinateSystemDefinition)
    {
        CoordinateSystem? outputCoordinateSystem;
        int? outputEpsg;
        string outputCrsLabel;
        string failureReason = string.Empty;

        if (coordinateMode == CoordinateExportMode.ConvertToTargetCrs)
        {
            outputEpsg = targetEpsg > 0 ? targetEpsg : null;
            outputCrsLabel = outputEpsg.HasValue
                ? CoordinateSystemCatalog.DescribeEpsg(outputEpsg.Value)
                : "Target CRS";

            if (!outputEpsg.HasValue ||
                !CoordinateSystemCatalog.TryCreateFromEpsg(outputEpsg.Value, out outputCoordinateSystem))
            {
                return new PreviewMapContext(
                    coordinateMode,
                    outputEpsg,
                    outputCrsLabel,
                    null,
                    null,
                    "Target EPSG could not be resolved for map preview.");
            }
        }
        else
        {
            outputEpsg = sourceEpsg;
            outputCrsLabel = BuildSharedOutputLabel(sourceEpsg, sourceCoordinateSystemId);

            if (!CoordinateSystemCatalog.TryCreateSourceCoordinateSystem(
                    sourceCoordinateSystemDefinition,
                    sourceCoordinateSystemId,
                    sourceEpsg,
                    out outputCoordinateSystem,
                    out failureReason))
            {
                return new PreviewMapContext(
                    coordinateMode,
                    outputEpsg,
                    outputCrsLabel,
                    null,
                    null,
                    failureReason.Length == 0
                        ? "Model CRS could not be resolved for map preview."
                        : failureReason);
            }
        }

        if (!CoordinateSystemCatalog.TryCreateWebMercator(out CoordinateSystem? displayCoordinateSystem))
        {
            return new PreviewMapContext(
                coordinateMode,
                outputEpsg,
                outputCrsLabel,
                outputCoordinateSystem,
                null,
                "Web Mercator could not be resolved for map preview.");
        }

        return new PreviewMapContext(
            coordinateMode,
            outputEpsg,
            outputCrsLabel,
            outputCoordinateSystem,
            displayCoordinateSystem,
            string.Empty);
    }

    private static string BuildSharedOutputLabel(int? sourceEpsg, string? sourceCoordinateSystemId)
    {
        if (sourceEpsg.HasValue)
        {
            return CoordinateSystemCatalog.DescribeEpsg(sourceEpsg.Value);
        }

        if (!string.IsNullOrWhiteSpace(sourceCoordinateSystemId))
        {
            return sourceCoordinateSystemId.Trim();
        }

        return "Shared coordinates";
    }
}
