using System;
using System.Collections.Generic;

namespace RevitGeoExporter.Core.Models;

public static class StairDetailLayout
{
    public static IReadOnlyList<double> BuildCenteredLinePositions(
        double pathLengthMeters,
        double spacingMeters)
    {
        if (pathLengthMeters <= 0d)
        {
            return Array.Empty<double>();
        }

        if (spacingMeters <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(spacingMeters), "Spacing must be positive.");
        }

        int lineCount = Math.Max(1, (int)Math.Floor(pathLengthMeters / spacingMeters));
        double occupiedLengthMeters = (lineCount - 1) * spacingMeters;
        double startDistanceMeters = Math.Max(0d, (pathLengthMeters - occupiedLengthMeters) * 0.5d);

        List<double> positions = new(lineCount);
        for (int i = 0; i < lineCount; i++)
        {
            positions.Add(startDistanceMeters + (i * spacingMeters));
        }

        return positions;
    }
}
