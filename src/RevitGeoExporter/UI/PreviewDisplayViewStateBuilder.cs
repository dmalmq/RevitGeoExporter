using System;
using System.Collections.Generic;
using System.Linq;
using ProjNet.CoordinateSystems;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;
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

        try
        {
            IReadOnlyList<PreviewFeatureData> outputFeatures = viewData.Features;
            Bounds2D outputBounds = viewData.Bounds;
            Point2D? outputSurveyPoint = request.SurveyPointSharedCoordinates;

            if (request.CoordinateMode == CoordinateExportMode.ConvertToTargetCrs &&
                mapContext.SourceCoordinateSystem != null &&
                mapContext.OutputCoordinateSystem != null)
            {
                outputFeatures = ReprojectFeatures(
                    viewData.Features,
                    mapContext.SourceCoordinateSystem,
                    mapContext.OutputCoordinateSystem);
                Bounds2D convertedBounds = FeatureBoundsCalculator.FromFeatures(outputFeatures.Select(feature => feature.Feature));
                outputBounds = convertedBounds.IsEmpty ? viewData.Bounds : convertedBounds;
                if (outputSurveyPoint.HasValue)
                {
                    outputSurveyPoint = CoordinateSystemCatalog.ReprojectPoint(
                        outputSurveyPoint.Value,
                        mapContext.SourceCoordinateSystem,
                        mapContext.OutputCoordinateSystem);
                }
            }

            if (!mapContext.CanShowBasemap)
            {
                return new PreviewDisplayViewState(
                    viewData,
                    outputFeatures,
                    outputBounds,
                    mapContext,
                    outputSurveyPoint,
                    outputSurveyPoint);
            }

            List<PreviewFeatureData> displayFeatures = ReprojectFeatures(
                outputFeatures,
                mapContext.OutputCoordinateSystem!,
                mapContext.DisplayCoordinateSystem!);
            Bounds2D displayBounds = FeatureBoundsCalculator.FromFeatures(displayFeatures.Select(feature => feature.Feature));
            Point2D? displaySurveyPoint = outputSurveyPoint.HasValue
                ? CoordinateSystemCatalog.ReprojectPoint(
                    outputSurveyPoint.Value,
                    mapContext.OutputCoordinateSystem!,
                    mapContext.DisplayCoordinateSystem!)
                : null;
            return new PreviewDisplayViewState(
                viewData,
                displayFeatures,
                displayBounds.IsEmpty ? outputBounds : displayBounds,
                mapContext,
                outputSurveyPoint,
                displaySurveyPoint);
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
            return new PreviewDisplayViewState(
                viewData,
                viewData.Features,
                viewData.Bounds,
                failedContext,
                request.SurveyPointSharedCoordinates,
                request.SurveyPointSharedCoordinates);
        }
    }

    private static List<PreviewFeatureData> ReprojectFeatures(
        IReadOnlyList<PreviewFeatureData> features,
        CoordinateSystem source,
        CoordinateSystem target)
    {
        return features
            .Select(feature => feature.WithFeature(CoordinateSystemCatalog.ReprojectFeature(feature.Feature, source, target)))
            .ToList();
    }
}