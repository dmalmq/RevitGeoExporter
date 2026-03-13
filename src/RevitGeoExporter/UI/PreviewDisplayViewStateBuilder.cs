using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Export;

namespace RevitGeoExporter.UI;

internal static class PreviewDisplayViewStateBuilder
{
    public static PreviewDisplayViewState Build(PreviewViewData viewData, ExportPreviewRequest request)
    {
        if (viewData is null)
        {
            throw new ArgumentNullException(nameof(viewData));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        PreviewMapContext mapContext = PreviewMapContextFactory.Create(
            request.CoordinateMode,
            request.TargetEpsg,
            request.SourceEpsg,
            request.SourceCoordinateSystemId,
            request.SourceCoordinateSystemDefinition);

        if (!mapContext.CanShowBasemap)
        {
            return new PreviewDisplayViewState(viewData, viewData.Features, viewData.Bounds, mapContext);
        }

        try
        {
            List<PreviewFeatureData> displayFeatures = viewData.Features
                .Select(feature => feature.WithFeature(mapContext.ProjectFeatureForDisplay(feature.Feature)))
                .ToList();
            Bounds2D displayBounds = FeatureBoundsCalculator.FromFeatures(displayFeatures.Select(feature => feature.Feature));
            return new PreviewDisplayViewState(
                viewData,
                displayFeatures,
                displayBounds.IsEmpty ? viewData.Bounds : displayBounds,
                mapContext);
        }
        catch
        {
            PreviewMapContext failedContext = new(
                mapContext.CoordinateMode,
                mapContext.SourceCoordinateSystem,
                mapContext.OutputEpsg,
                mapContext.OutputCrsLabel,
                mapContext.OutputCoordinateSystem,
                mapContext.DisplayCoordinateSystem,
                "Map preview projection failed.");
            return new PreviewDisplayViewState(viewData, viewData.Features, viewData.Bounds, failedContext);
        }
    }
}
