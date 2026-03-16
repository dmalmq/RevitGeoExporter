using System.Collections.Generic;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Export;

namespace RevitGeoExporter.UI;

internal sealed class PreviewDisplayViewState
{
    public PreviewDisplayViewState(
        PreviewViewData sourceViewData,
        IReadOnlyList<PreviewFeatureData> displayFeatures,
        Bounds2D displayBounds,
        PreviewMapContext mapContext,
        Point2D? outputSurveyPoint,
        Point2D? displaySurveyPoint)
    {
        SourceViewData = sourceViewData;
        DisplayFeatures = displayFeatures;
        DisplayBounds = displayBounds;
        MapContext = mapContext;
        OutputSurveyPoint = outputSurveyPoint;
        DisplaySurveyPoint = displaySurveyPoint;
    }

    public PreviewViewData SourceViewData { get; }

    public IReadOnlyList<PreviewFeatureData> DisplayFeatures { get; }

    public Bounds2D DisplayBounds { get; }

    public PreviewMapContext MapContext { get; }

    public Point2D? OutputSurveyPoint { get; }

    public Point2D? DisplaySurveyPoint { get; }
}