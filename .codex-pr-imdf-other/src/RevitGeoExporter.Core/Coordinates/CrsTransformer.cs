using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Coordinates;

public sealed class CrsTransformer
{
    public const double FeetToMetersFactor = 0.3048d;

    public Point2D TransformFromRevitFeet(
        double xFeet,
        double yFeet,
        double offsetXMeters,
        double offsetYMeters,
        double rotationDegrees)
    {
        double xMeters = xFeet * FeetToMetersFactor;
        double yMeters = yFeet * FeetToMetersFactor;
        return ApplyOffsetAndRotation(new Point2D(xMeters, yMeters), offsetXMeters, offsetYMeters, rotationDegrees);
    }

    public IReadOnlyList<Point2D> TransformFromRevitFeet(
        IReadOnlyList<Point2D> pointsInFeet,
        double offsetXMeters,
        double offsetYMeters,
        double rotationDegrees)
    {
        if (pointsInFeet is null)
        {
            throw new ArgumentNullException(nameof(pointsInFeet));
        }

        List<Point2D> transformed = new(pointsInFeet.Count);
        foreach (Point2D point in pointsInFeet)
        {
            transformed.Add(
                TransformFromRevitFeet(
                    point.X,
                    point.Y,
                    offsetXMeters,
                    offsetYMeters,
                    rotationDegrees));
        }

        return transformed;
    }

    public Point2D ApplyOffsetAndRotation(
        Point2D pointMeters,
        double offsetXMeters,
        double offsetYMeters,
        double rotationDegrees)
    {
        double radians = rotationDegrees * (Math.PI / 180d);
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        double xRot = (pointMeters.X * cos) - (pointMeters.Y * sin);
        double yRot = (pointMeters.X * sin) + (pointMeters.Y * cos);

        return new Point2D(xRot + offsetXMeters, yRot + offsetYMeters);
    }
}
