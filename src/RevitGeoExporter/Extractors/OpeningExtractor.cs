using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Extractors;

public sealed class OpeningExtractor
{
    private readonly Document _document;
    private readonly Transform _internalToSharedTransform;
    private readonly CrsTransformer _transformer;
    private readonly SharedParameterManager _parameterManager;

    public OpeningExtractor(Document document, SharedParameterManager parameterManager)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _parameterManager = parameterManager ?? throw new ArgumentNullException(nameof(parameterManager));
        _internalToSharedTransform =
            _document.ActiveProjectLocation?.GetTotalTransform() ?? Transform.Identity;
        _transformer = new CrsTransformer();
    }

    public IReadOnlyList<ExportLineString> ExtractForLevel(
        Level level,
        string levelId,
        IReadOnlyList<FamilyInstance> openingInstances,
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

        if (openingInstances is null)
        {
            throw new ArgumentNullException(nameof(openingInstances));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        List<ExportLineString> features = new();
        foreach (FamilyInstance opening in openingInstances)
        {
            if (!IsOnLevel(opening, level))
            {
                continue;
            }

            if (!TryExtractOpeningLine(opening, out LineString2D lineString))
            {
                warnings.Add($"Opening {opening.Id.Value} geometry could not be extracted.");
                continue;
            }

            string id = _parameterManager.GetOrCreateElementId(opening, warnings);
            features.Add(
                new ExportLineString(
                    lineString,
                    new Dictionary<string, object?>
                    {
                        ["id"] = id,
                        ["category"] = GetOpeningCategory(opening),
                        ["level_id"] = levelId,
                        ["element_id"] = opening.Id.Value,
                    }));
        }

        return features;
    }

    private bool TryExtractOpeningLine(FamilyInstance opening, out LineString2D lineString)
    {
        lineString = null!;

        if (opening.Location is LocationCurve locationCurve && locationCurve.Curve != null)
        {
            return TryExtractFromCurve(locationCurve.Curve, out lineString);
        }

        if (opening.Location is LocationPoint locationPoint)
        {
            XYZ center = locationPoint.Point;
            double widthFeet = GetOpeningWidthFeet(opening);
            XYZ axis = GetOpeningAxis(opening);
            XYZ start = center - (axis * (widthFeet * 0.5d));
            XYZ end = center + (axis * (widthFeet * 0.5d));
            return TryCreateLineString(new[] { start, end }, out lineString);
        }

        BoundingBoxXYZ? box = opening.get_BoundingBox(null);
        if (box == null)
        {
            return false;
        }

        double centerZ = (box.Min.Z + box.Max.Z) * 0.5d;
        double spanX = Math.Abs(box.Max.X - box.Min.X);
        double spanY = Math.Abs(box.Max.Y - box.Min.Y);
        XYZ startPoint;
        XYZ endPoint;
        if (spanX >= spanY)
        {
            double centerY = (box.Min.Y + box.Max.Y) * 0.5d;
            startPoint = new XYZ(box.Min.X, centerY, centerZ);
            endPoint = new XYZ(box.Max.X, centerY, centerZ);
        }
        else
        {
            double centerX = (box.Min.X + box.Max.X) * 0.5d;
            startPoint = new XYZ(centerX, box.Min.Y, centerZ);
            endPoint = new XYZ(centerX, box.Max.Y, centerZ);
        }

        return TryCreateLineString(new[] { startPoint, endPoint }, out lineString);
    }

    private bool TryExtractFromCurve(Curve curve, out LineString2D lineString)
    {
        IList<XYZ> tessellated = curve.Tessellate();
        if (tessellated.Count < 2)
        {
            tessellated = new[]
            {
                curve.GetEndPoint(0),
                curve.GetEndPoint(1),
            };
        }

        return TryCreateLineString(tessellated, out lineString);
    }

    private bool TryCreateLineString(IList<XYZ> points3d, out LineString2D lineString)
    {
        lineString = null!;
        List<Point2D> points = new(points3d.Count);
        for (int i = 0; i < points3d.Count; i++)
        {
            XYZ sharedPoint = _internalToSharedTransform.OfPoint(points3d[i]);
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

    private static double GetOpeningWidthFeet(FamilyInstance opening)
    {
        double width = TryReadWidth(opening.LookupParameter("Width"));
        if (width > 1e-6d)
        {
            return width;
        }

        if (opening.Symbol != null)
        {
            width = TryReadWidth(opening.Symbol.LookupParameter("Width"));
            if (width > 1e-6d)
            {
                return width;
            }
        }

        BoundingBoxXYZ? box = opening.get_BoundingBox(null);
        if (box != null)
        {
            double spanX = Math.Abs(box.Max.X - box.Min.X);
            double spanY = Math.Abs(box.Max.Y - box.Min.Y);
            double fallback = Math.Max(spanX, spanY);
            if (fallback > 1e-6d)
            {
                return fallback;
            }
        }

        return 3.0d;
    }

    private static double TryReadWidth(Parameter? parameter)
    {
        if (parameter == null || parameter.StorageType != StorageType.Double)
        {
            return 0d;
        }

        return parameter.AsDouble();
    }

    private static XYZ GetOpeningAxis(FamilyInstance opening)
    {
        XYZ axis = new XYZ(opening.HandOrientation.X, opening.HandOrientation.Y, 0d);
        if (axis.GetLength() > 1e-6d)
        {
            return axis.Normalize();
        }

        XYZ facing = opening.FacingOrientation;
        XYZ perpendicular = new XYZ(-facing.Y, facing.X, 0d);
        if (perpendicular.GetLength() > 1e-6d)
        {
            return perpendicular.Normalize();
        }

        return XYZ.BasisX;
    }

    private static string GetOpeningCategory(FamilyInstance opening)
    {
        string familyName = opening.Symbol?.FamilyName ?? opening.Name ?? string.Empty;
        return familyName.IndexOf("emergency", StringComparison.OrdinalIgnoreCase) >= 0
            ? "emergencyexit"
            : "pedestrian";
    }

    private static bool IsOnLevel(FamilyInstance opening, Level level)
    {
        if (opening.LevelId == level.Id)
        {
            return true;
        }

        Parameter? levelParam = opening.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
        if (levelParam != null &&
            levelParam.StorageType == StorageType.ElementId &&
            levelParam.AsElementId() == level.Id)
        {
            return true;
        }

        BoundingBoxXYZ? box = opening.get_BoundingBox(null);
        if (box == null)
        {
            return false;
        }

        const double toleranceFeet = 1.0d;
        return level.Elevation >= box.Min.Z - toleranceFeet &&
               level.Elevation <= box.Max.Z + toleranceFeet;
    }

    private static bool IsSamePoint(Point2D left, Point2D right)
    {
        return Math.Abs(left.X - right.X) <= 1e-8d &&
               Math.Abs(left.Y - right.Y) <= 1e-8d;
    }
}
