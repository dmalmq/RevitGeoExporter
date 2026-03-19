using System;
using ProjNet.CoordinateSystems;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Preview;

public sealed class PreviewMapContext
{
    public PreviewMapContext(
        CoordinateExportMode coordinateMode,
        CoordinateSystem? sourceCoordinateSystem,
        int? outputEpsg,
        string outputCrsLabel,
        CoordinateSystem? outputCoordinateSystem,
        CoordinateSystem? displayCoordinateSystem,
        string? unavailableReason)
    {
        CoordinateMode = coordinateMode;
        SourceCoordinateSystem = sourceCoordinateSystem;
        OutputEpsg = outputEpsg;
        OutputCrsLabel = outputCrsLabel ?? string.Empty;
        OutputCoordinateSystem = outputCoordinateSystem;
        DisplayCoordinateSystem = displayCoordinateSystem;
        UnavailableReason = unavailableReason ?? string.Empty;
    }

    public CoordinateExportMode CoordinateMode { get; }

    public CoordinateSystem? SourceCoordinateSystem { get; }

    public int? OutputEpsg { get; }

    public string OutputCrsLabel { get; }

    public CoordinateSystem? OutputCoordinateSystem { get; }

    public CoordinateSystem? DisplayCoordinateSystem { get; }

    public string UnavailableReason { get; }

    public bool CanShowBasemap =>
        SourceCoordinateSystem != null &&
        OutputCoordinateSystem != null &&
        DisplayCoordinateSystem != null &&
        UnavailableReason.Length == 0;

    public IExportFeature ProjectFeatureForDisplay(IExportFeature feature)
    {
        if (feature is null)
        {
            throw new ArgumentNullException(nameof(feature));
        }

        if (!CanShowBasemap)
        {
            throw new InvalidOperationException("Map preview is unavailable for the current coordinate system configuration.");
        }

        IExportFeature transformed = feature;
        if (!ReferenceEquals(SourceCoordinateSystem, OutputCoordinateSystem))
        {
            transformed = CoordinateSystemCatalog.ReprojectFeature(
                transformed,
                SourceCoordinateSystem!,
                OutputCoordinateSystem!);
        }

        if (!ReferenceEquals(OutputCoordinateSystem, DisplayCoordinateSystem))
        {
            transformed = CoordinateSystemCatalog.ReprojectFeature(
                transformed,
                OutputCoordinateSystem!,
                DisplayCoordinateSystem!);
        }

        return transformed;
    }
}
