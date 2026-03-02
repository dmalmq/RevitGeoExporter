using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Extractors;

public sealed class DetailExtractor
{
    private readonly Document _document;
    private readonly Transform _internalToSharedTransform;
    private readonly CrsTransformer _transformer;

    public DetailExtractor(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _internalToSharedTransform =
            _document.ActiveProjectLocation?.GetTotalTransform() ?? Transform.Identity;
        _transformer = new CrsTransformer();
    }

    public IReadOnlyList<ExportLineString> ExtractForLevel(
        Level level,
        string levelId,
        IReadOnlyList<CurveElement> detailCurves,
        ICollection<string> warnings)
    {
        if (level is null)
        {
            throw new ArgumentNullException(nameof(level));
        }

        if (string.IsNullOrWhiteSpace(levelId))
        {
            throw new ArgumentException("Level id is required.", nameof(levelId));
        }

        if (detailCurves is null)
        {
            throw new ArgumentNullException(nameof(detailCurves));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        List<ExportLineString> features = new();
        foreach (CurveElement curveElement in detailCurves)
        {
            if (!IsOnLevel(curveElement, level))
            {
                continue;
            }

            if (!TryExtractLineString(curveElement, out LineString2D lineString))
            {
                warnings.Add($"Detail line {curveElement.Id.Value} geometry could not be extracted.");
                continue;
            }

            features.Add(
                new ExportLineString(
                    lineString,
                    new Dictionary<string, object?>
                    {
                        ["id"] = StableIdGenerator.Create("detail", curveElement.Id.Value, levelId),
                        ["level_id"] = levelId,
                        ["element_id"] = curveElement.Id.Value,
                    }));
        }

        return features;
    }

    private bool TryExtractLineString(CurveElement curveElement, out LineString2D lineString)
    {
        lineString = null!;
        Curve? curve = curveElement.GeometryCurve;
        if (curve == null)
        {
            return false;
        }

        IList<XYZ> tessellated = curve.Tessellate();
        if (tessellated.Count < 2)
        {
            tessellated = new[]
            {
                curve.GetEndPoint(0),
                curve.GetEndPoint(1),
            };
        }

        List<Point2D> points = new(tessellated.Count);
        for (int i = 0; i < tessellated.Count; i++)
        {
            XYZ sharedPoint = _internalToSharedTransform.OfPoint(tessellated[i]);
            Point2D point = _transformer.TransformFromRevitFeet(
                sharedPoint.X,
                sharedPoint.Y,
                offsetXMeters: 0d,
                offsetYMeters: 0d,
                rotationDegrees: 0d);

            if (points.Count == 0 || !IsSamePoint(points[points.Count - 1], point))
            {
                points.Add(point);
            }
        }

        if (points.Count < 2)
        {
            return false;
        }

        lineString = new LineString2D(points);
        return true;
    }

    private bool IsOnLevel(CurveElement curveElement, Level level)
    {
        if (curveElement.OwnerViewId != ElementId.InvalidElementId)
        {
            if (_document.GetElement(curveElement.OwnerViewId) is ViewPlan plan && plan.GenLevel != null)
            {
                return plan.GenLevel.Id == level.Id;
            }

            return false;
        }

        BoundingBoxXYZ? box = curveElement.get_BoundingBox(null);
        if (box != null)
        {
            const double toleranceFeet = 1.0d;
            return level.Elevation >= box.Min.Z - toleranceFeet &&
                   level.Elevation <= box.Max.Z + toleranceFeet;
        }

        Curve? curve = curveElement.GeometryCurve;
        if (curve == null)
        {
            return false;
        }

        XYZ p0 = curve.GetEndPoint(0);
        XYZ p1 = curve.GetEndPoint(1);
        double averageZ = (p0.Z + p1.Z) * 0.5d;
        return Math.Abs(averageZ - level.Elevation) <= 1.0d;
    }

    private static bool IsSamePoint(Point2D left, Point2D right)
    {
        return Math.Abs(left.X - right.X) <= 1e-8d &&
               Math.Abs(left.Y - right.Y) <= 1e-8d;
    }
}
