using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Preview;

public static class FeatureBoundsCalculator
{
    public static Bounds2D FromFeatures(IEnumerable<IExportFeature> features)
    {
        if (features is null)
        {
            throw new ArgumentNullException(nameof(features));
        }

        bool hasPoint = false;
        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        foreach (IExportFeature feature in features)
        {
            if (feature == null)
            {
                continue;
            }

            foreach (Point2D point in feature.GetAllPoints())
            {
                hasPoint = true;
                if (point.X < minX)
                {
                    minX = point.X;
                }

                if (point.Y < minY)
                {
                    minY = point.Y;
                }

                if (point.X > maxX)
                {
                    maxX = point.X;
                }

                if (point.Y > maxY)
                {
                    maxY = point.Y;
                }
            }
        }

        return hasPoint ? new Bounds2D(minX, minY, maxX, maxY) : Bounds2D.Empty;
    }
}
