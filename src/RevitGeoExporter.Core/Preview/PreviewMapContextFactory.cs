using ProjNet.CoordinateSystems;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Preview;

public static class PreviewMapContextFactory
{
    public const string ConvertModeMissingSourceReason =
        "Model source CRS could not be resolved for preview conversion.";

    public static PreviewMapContext Create(
        CoordinateExportMode coordinateMode,
        int targetEpsg,
        int? sourceEpsg,
        string? sourceCoordinateSystemId,
        string? sourceCoordinateSystemDefinition)
    {
        CoordinateSystemCatalog.TryCreateSourceCoordinateSystem(
            sourceCoordinateSystemDefinition,
            sourceCoordinateSystemId,
            sourceEpsg,
            out CoordinateSystem? sourceCoordinateSystem,
            out string sourceFailureReason);

        CoordinateSystem? outputCoordinateSystem;
        int? outputEpsg;
        string outputCrsLabel;

        if (coordinateMode == CoordinateExportMode.ConvertToTargetCrs)
        {
            outputEpsg = targetEpsg > 0 ? targetEpsg : null;
            outputCrsLabel = outputEpsg.HasValue
                ? CoordinateSystemCatalog.DescribeEpsg(outputEpsg.Value)
                : "Target CRS";

            if (sourceCoordinateSystem == null)
            {
                return new PreviewMapContext(
                    coordinateMode,
                    null,
                    outputEpsg,
                    outputCrsLabel,
                    null,
                    null,
                    ConvertModeMissingSourceReason);
            }

            if (!outputEpsg.HasValue)
            {
                return new PreviewMapContext(
                    coordinateMode,
                    sourceCoordinateSystem,
                    outputEpsg,
                    outputCrsLabel,
                    null,
                    null,
                    "Target EPSG could not be resolved for map preview.");
            }

            if (sourceEpsg.HasValue && sourceEpsg.Value == outputEpsg.Value)
            {
                outputCoordinateSystem = sourceCoordinateSystem;
            }
            else if (!CoordinateSystemCatalog.TryCreateFromEpsg(outputEpsg.Value, out outputCoordinateSystem))
            {
                return new PreviewMapContext(
                    coordinateMode,
                    sourceCoordinateSystem,
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

            if (sourceCoordinateSystem == null)
            {
                return new PreviewMapContext(
                    coordinateMode,
                    null,
                    outputEpsg,
                    outputCrsLabel,
                    null,
                    null,
                    sourceFailureReason.Length == 0
                        ? "Model CRS could not be resolved for map preview."
                        : sourceFailureReason);
            }

            outputCoordinateSystem = sourceCoordinateSystem;
        }

        if (!CoordinateSystemCatalog.TryCreateWebMercator(out CoordinateSystem? displayCoordinateSystem))
        {
            return new PreviewMapContext(
                coordinateMode,
                sourceCoordinateSystem,
                outputEpsg,
                outputCrsLabel,
                outputCoordinateSystem,
                null,
                "Web Mercator could not be resolved for map preview.");
        }

        return new PreviewMapContext(
            coordinateMode,
            sourceCoordinateSystem,
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
            return sourceCoordinateSystemId?.Trim() ?? string.Empty;
        }

        return "Shared coordinates";
    }
}
