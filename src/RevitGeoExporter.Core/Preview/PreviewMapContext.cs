using ProjNet.CoordinateSystems;
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
        OutputCoordinateSystem != null &&
        DisplayCoordinateSystem != null &&
        UnavailableReason.Length == 0;
}