using System.Collections.Generic;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Export;

namespace RevitGeoExporter.UI;

internal sealed class PreviewDisplayViewState
{
    public PreviewDisplayViewState(
        PreviewViewData sourceViewData,
        IReadOnlyList<PreviewFeatureData> displayFeatures,
        Bounds2D displayBounds,
        PreviewMapContext mapContext)
    {
        SourceViewData = sourceViewData;
        DisplayFeatures = displayFeatures;
        DisplayBounds = displayBounds;
        MapContext = mapContext;
    }

    public PreviewViewData SourceViewData { get; }

    public IReadOnlyList<PreviewFeatureData> DisplayFeatures { get; }

    public Bounds2D DisplayBounds { get; }

    public PreviewMapContext MapContext { get; }
}
