using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Extractors;

public sealed class OpeningExtractor
{
    private const double FeetToMeters = CrsTransformer.FeetToMetersFactor;
    private const double MinOpeningLengthMeters = 0.10d;
    private const double EndpointInsetMeters = 0.05d;
    private const double MaxOutlineSnapDistanceMeters = 5.0d;
    private const double MaxOpeningSnapDistanceMeters = 0.20d;
    private const double MaxElevatorOpeningSnapDistanceMeters = 0.50d;
    private const double StairLevelElevationToleranceFeet = 0.75d;

    private readonly Document _document;
    private readonly Transform _internalToSharedTransform;
    private readonly CrsTransformer _transformer;
    private readonly SharedParameterManager _parameterManager;
    private readonly ZoneCatalog _zoneCatalog;

    public OpeningExtractor(
        Document document,
        SharedParameterManager parameterManager,
        ZoneCatalog zoneCatalog)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _parameterManager = parameterManager ?? throw new ArgumentNullException(nameof(parameterManager));
        _zoneCatalog = zoneCatalog ?? throw new ArgumentNullException(nameof(zoneCatalog));
        _internalToSharedTransform =
            _document.ActiveProjectLocation?.GetTotalTransform() ?? Transform.Identity;
        _transformer = new CrsTransformer();
    }

    public IReadOnlyList<ExportLineString> ExtractForLevel(
        Level level,
        string levelId,
        IReadOnlyList<FamilyInstance> openingInstances,
        IReadOnlyList<Stairs> stairs,
        IReadOnlyList<FamilyInstance> familyUnits,
        IReadOnlyList<ExportPolygon> unitFeatures,
        IReadOnlyList<LineString2D> elevatorOpeningLines,
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

        if (stairs is null)
        {
            throw new ArgumentNullException(nameof(stairs));
        }

        if (familyUnits is null)
        {
            throw new ArgumentNullException(nameof(familyUnits));
        }

        if (unitFeatures is null)
        {
            throw new ArgumentNullException(nameof(unitFeatures));
        }

        if (elevatorOpeningLines is null)
        {
            throw new ArgumentNullException(nameof(elevatorOpeningLines));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        List<ExportLineString> features = new();
        HashSet<string> seenGeometryKeys = new(StringComparer.Ordinal);
        List<BoundarySegment> snapSegments = BuildSnapSegments(unitFeatures);

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

            lineString = SnapToClosestOutline(lineString, snapSegments, MaxOpeningSnapDistanceMeters);

            string id = _parameterManager.GetOrCreateElementId(opening, warnings);
            AddFeature(
                features,
                seenGeometryKeys,
                lineString,
                id,
                GetOpeningCategory(opening),
                levelId,
                opening.Id.Value);
        }

        AddStairEntrances(features, seenGeometryKeys, stairs, level.Elevation, levelId, warnings);
        AddEscalatorEntrances(features, seenGeometryKeys, familyUnits, snapSegments, levelId);
        AddElevatorEntrances(features, seenGeometryKeys, elevatorOpeningLines, levelId);
        return features;
    }

    private void AddStairEntrances(
        ICollection<ExportLineString> target,
        ISet<string> seenGeometryKeys,
        IReadOnlyList<Stairs> stairs,
        double levelElevationFeet,
        string levelId,
        ICollection<string> warnings)
    {
        foreach (Stairs stair in stairs)
        {
            LineString2D? preferredLine = null;
            double preferredScore = double.MaxValue;
            LineString2D? fallbackLine = null;
            double fallbackScore = double.MaxValue;

            foreach (ElementId runId in stair.GetStairsRuns())
            {
                if (_document.GetElement(runId) is not StairsRun run)
                {
                    continue;
                }

                if (!TryGetRunEndpoints(run, out Point2D start, out Point2D end, out Point2D direction, warnings))
                {
                    continue;
                }

                double runWidthMeters = Math.Max(0.5d, run.ActualRunWidth * FeetToMeters);
                double halfWidthMeters = runWidthMeters * 0.5d;

                double baseDelta = Math.Abs(run.BaseElevation - levelElevationFeet);
                bool baseMatchesLevel = baseDelta <= StairLevelElevationToleranceFeet;
                if (TryCreateEntranceLine(start, direction, EndpointInsetMeters, halfWidthMeters, out LineString2D startLine))
                {
                    if (baseMatchesLevel)
                    {
                        TryPromoteCandidate(startLine, baseDelta, ref preferredLine, ref preferredScore);
                    }
                    else
                    {
                        TryPromoteCandidate(startLine, baseDelta, ref fallbackLine, ref fallbackScore);
                    }
                }

                double topDelta = Math.Abs(run.TopElevation - levelElevationFeet);
                bool topMatchesLevel = topDelta <= StairLevelElevationToleranceFeet;
                if (TryCreateEntranceLine(end, direction, -EndpointInsetMeters, halfWidthMeters, out LineString2D endLine))
                {
                    if (topMatchesLevel)
                    {
                        TryPromoteCandidate(endLine, topDelta, ref preferredLine, ref preferredScore);
                    }
                    else
                    {
                        TryPromoteCandidate(endLine, topDelta, ref fallbackLine, ref fallbackScore);
                    }
                }
            }

            LineString2D? chosen = preferredLine ?? fallbackLine;
            if (chosen != null)
            {
                string id = StableIdGenerator.Create("opening.stair", stair.Id.Value, levelId);
                AddFeature(target, seenGeometryKeys, chosen, id, "pedestrian", levelId, stair.Id.Value);
            }
        }
    }

    private static void TryPromoteCandidate(
        LineString2D candidate,
        double score,
        ref LineString2D? currentBest,
        ref double currentBestScore)
    {
        if (candidate == null)
        {
            return;
        }

        if (currentBest == null || score < currentBestScore)
        {
            currentBest = candidate;
            currentBestScore = score;
        }
    }

    private void AddEscalatorEntrances(
        ICollection<ExportLineString> target,
        ISet<string> seenGeometryKeys,
        IReadOnlyList<FamilyInstance> familyUnits,
        IReadOnlyList<BoundarySegment> snapSegments,
        string levelId)
    {
        foreach (FamilyInstance escalator in familyUnits)
        {
            if (!IsEscalatorFamily(escalator))
            {
                continue;
            }

            if (!TryGetEscalatorEndpoints(
                    escalator,
                    out Point2D start,
                    out Point2D end,
                    out Point2D direction,
                    out double halfWidthMeters))
            {
                continue;
            }

            if (TryCreateEntranceLine(start, direction, EndpointInsetMeters, halfWidthMeters, out LineString2D startLine))
            {
                LineString2D snapped = SnapToClosestOutline(startLine, snapSegments);
                string id = StableIdGenerator.Create("opening.escalator", (escalator.Id.Value * 10L) + 1, levelId);
                AddFeature(target, seenGeometryKeys, snapped, id, "pedestrian", levelId, escalator.Id.Value);
            }

            if (TryCreateEntranceLine(end, direction, -EndpointInsetMeters, halfWidthMeters, out LineString2D endLine))
            {
                LineString2D snapped = SnapToClosestOutline(endLine, snapSegments);
                string id = StableIdGenerator.Create("opening.escalator", (escalator.Id.Value * 10L) + 2, levelId);
                AddFeature(target, seenGeometryKeys, snapped, id, "pedestrian", levelId, escalator.Id.Value);
            }
        }
    }

    private void AddElevatorEntrances(
        List<ExportLineString> target,
        ISet<string> seenGeometryKeys,
        IReadOnlyList<LineString2D> elevatorOpeningLines,
        string levelId)
    {
        for (int i = 0; i < elevatorOpeningLines.Count; i++)
        {
            LineString2D line = elevatorOpeningLines[i];
            if (line.Points.Count < 2)
            {
                continue;
            }

            double dx = line.Points[1].X - line.Points[0].X;
            double dy = line.Points[1].Y - line.Points[0].Y;
            if (Math.Sqrt((dx * dx) + (dy * dy)) < MinOpeningLengthMeters)
            {
                continue;
            }

            Point2D elevatorMidpoint = Midpoint(line);
            int existingIndex = FindNearestExistingOpening(
                target, elevatorMidpoint, MaxElevatorOpeningSnapDistanceMeters);

            if (existingIndex >= 0)
            {
                ExportLineString existing = target[existingIndex];
                string oldGeometryKey = BuildGeometryKey(existing.LineString);
                seenGeometryKeys.Remove(oldGeometryKey);
                target.RemoveAt(existingIndex);

                string newGeometryKey = BuildGeometryKey(line);
                if (seenGeometryKeys.Add(newGeometryKey))
                {
                    Dictionary<string, object?> snappedAttributes = new();
                    foreach (KeyValuePair<string, object?> kvp in existing.Attributes)
                    {
                        snappedAttributes[kvp.Key] = kvp.Value;
                    }

                    target.Add(new ExportLineString(line, snappedAttributes));
                }
            }
            else
            {
                string id = StableIdGenerator.Create("opening.elevator", i + 1, levelId);
                AddFeature(target, seenGeometryKeys, line, id, "pedestrian", levelId, elementId: 0L);
            }
        }
    }

    private static int FindNearestExistingOpening(
        List<ExportLineString> features,
        Point2D elevatorOpeningMidpoint,
        double maxDistance)
    {
        int bestIndex = -1;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < features.Count; i++)
        {
            Point2D mid = Midpoint(features[i].LineString);
            double dist = Distance(mid, elevatorOpeningMidpoint);
            if (dist < bestDistance && dist <= maxDistance)
            {
                bestDistance = dist;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static Point2D Midpoint(LineString2D line)
    {
        Point2D start = line.Points[0];
        Point2D end = line.Points[line.Points.Count - 1];
        return new Point2D((start.X + end.X) * 0.5d, (start.Y + end.Y) * 0.5d);
    }

    private static List<BoundarySegment> BuildSnapSegments(IReadOnlyList<ExportPolygon> unitFeatures)
    {
        List<BoundarySegment> segments = new();
        foreach (ExportPolygon feature in unitFeatures)
        {
            if (TryGetCategory(feature, out string category) &&
                string.Equals(category, "escalator", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (Polygon2D polygon in feature.Polygons)
            {
                AddRingSegments(segments, polygon.ExteriorRing);
                for (int i = 0; i < polygon.InteriorRings.Count; i++)
                {
                    AddRingSegments(segments, polygon.InteriorRings[i]);
                }
            }
        }

        return segments;
    }

    private static void AddRingSegments(ICollection<BoundarySegment> segments, IReadOnlyList<Point2D> ring)
    {
        if (ring == null || ring.Count < 2)
        {
            return;
        }

        for (int i = 0; i < ring.Count - 1; i++)
        {
            Point2D a = ring[i];
            Point2D b = ring[i + 1];
            if (Distance(a, b) < MinOpeningLengthMeters)
            {
                continue;
            }

            segments.Add(new BoundarySegment(a, b));
        }
    }

    private static LineString2D SnapToClosestOutline(LineString2D line, IReadOnlyList<BoundarySegment> segments)
    {
        return SnapToClosestOutline(line, segments, MaxOutlineSnapDistanceMeters);
    }

    private static LineString2D SnapToClosestOutline(
        LineString2D line, IReadOnlyList<BoundarySegment> segments, double maxDistance)
    {
        if (segments == null || segments.Count == 0 || line.Points.Count < 2)
        {
            return line;
        }

        Point2D start = line.Points[0];
        Point2D end = line.Points[line.Points.Count - 1];
        Point2D center = new((start.X + end.X) * 0.5d, (start.Y + end.Y) * 0.5d);
        double length = Distance(start, end);
        if (length < MinOpeningLengthMeters)
        {
            return line;
        }

        BoundarySegment? nearest = null;
        double bestDistance = double.MaxValue;
        Point2D projected = default;
        double projectedT = 0d;
        for (int i = 0; i < segments.Count; i++)
        {
            BoundarySegment candidate = segments[i];
            Point2D candidatePoint = ProjectPointOntoSegment(center, candidate.Start, candidate.End, out double t);
            double distance = Distance(center, candidatePoint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = candidate;
                projected = candidatePoint;
                projectedT = t;
            }
        }

        if (nearest == null || bestDistance > maxDistance)
        {
            return line;
        }

        BoundarySegment segment = nearest.Value;
        if (!TryNormalize(segment.End.X - segment.Start.X, segment.End.Y - segment.Start.Y, out Point2D dir))
        {
            return line;
        }

        double segmentLength = Distance(segment.Start, segment.End);
        double halfLength = Math.Min(length * 0.5d, (segmentLength * 0.5d) - 0.01d);
        double distanceToStart = projectedT;
        double distanceToEnd = segmentLength - projectedT;
        halfLength = Math.Min(halfLength, Math.Min(distanceToStart, distanceToEnd));
        if (halfLength < MinOpeningLengthMeters * 0.5d)
        {
            return line;
        }

        Point2D snappedStart = new(projected.X - (dir.X * halfLength), projected.Y - (dir.Y * halfLength));
        Point2D snappedEnd = new(projected.X + (dir.X * halfLength), projected.Y + (dir.Y * halfLength));
        return new LineString2D(new[] { snappedStart, snappedEnd });
    }

    private bool TryGetRunEndpoints(
        StairsRun run,
        out Point2D start,
        out Point2D end,
        out Point2D direction,
        ICollection<string> warnings)
    {
        start = default;
        end = default;
        direction = default;

        CurveLoop path;
        try
        {
            path = run.GetStairsPath();
        }
        catch (Exception)
        {
            warnings.Add($"Stairs run {run.Id.Value} path could not be read for opening generation.");
            return false;
        }

        List<Point2D> points = ProjectCurveLoop(path, closeLoop: false);
        if (points.Count < 2)
        {
            return false;
        }

        start = points[0];
        end = points[points.Count - 1];
        return TryNormalize(end.X - start.X, end.Y - start.Y, out direction);
    }

    private bool TryGetEscalatorEndpoints(
        FamilyInstance escalator,
        out Point2D start,
        out Point2D end,
        out Point2D direction,
        out double halfWidthMeters)
    {
        start = default;
        end = default;
        direction = default;
        halfWidthMeters = 0d;

        if (escalator.Location is LocationCurve locationCurve && locationCurve.Curve != null)
        {
            List<Point2D> points = ProjectCurve(locationCurve.Curve);
            if (points.Count < 2)
            {
                return false;
            }

            start = points[0];
            end = points[points.Count - 1];
            if (!TryNormalize(end.X - start.X, end.Y - start.Y, out direction))
            {
                return false;
            }

            double widthFromParamMeters = GetOpeningWidthFeet(escalator) * FeetToMeters;
            halfWidthMeters = Math.Max(0.35d, widthFromParamMeters * 0.5d);
            return true;
        }

        if (escalator.Location is not LocationPoint locationPoint)
        {
            return false;
        }

        BoundingBoxXYZ? box = escalator.get_BoundingBox(null);
        if (box == null)
        {
            return false;
        }

        Point2D center = ProjectPoint(locationPoint.Point);
        Point2D axis = GetEscalatorAxis(escalator);
        Point2D perpendicular = new(-axis.Y, axis.X);

        List<Point2D> corners = GetBoundingBoxCorners(box)
            .Select(ProjectPoint)
            .ToList();

        GetAxisExtent(corners, center, axis, out double minAlong, out double maxAlong);
        GetAxisExtent(corners, center, perpendicular, out double minAcross, out double maxAcross);

        double lengthMeters = maxAlong - minAlong;
        double widthMeters = maxAcross - minAcross;
        if (lengthMeters <= 0.05d || widthMeters <= 0.05d)
        {
            return false;
        }

        direction = axis;
        start = new Point2D(center.X + (axis.X * minAlong), center.Y + (axis.Y * minAlong));
        end = new Point2D(center.X + (axis.X * maxAlong), center.Y + (axis.Y * maxAlong));
        halfWidthMeters = Math.Max(0.35d, widthMeters * 0.5d);
        return true;
    }

    private static void GetAxisExtent(
        IReadOnlyList<Point2D> points,
        Point2D center,
        Point2D axis,
        out double min,
        out double max)
    {
        min = double.MaxValue;
        max = double.MinValue;
        for (int i = 0; i < points.Count; i++)
        {
            double dx = points[i].X - center.X;
            double dy = points[i].Y - center.Y;
            double projection = (dx * axis.X) + (dy * axis.Y);
            if (projection < min)
            {
                min = projection;
            }

            if (projection > max)
            {
                max = projection;
            }
        }
    }

    private Point2D GetEscalatorAxis(FamilyInstance escalator)
    {
        if (TryNormalize(escalator.FacingOrientation.X, escalator.FacingOrientation.Y, out Point2D facing))
        {
            return facing;
        }

        if (TryNormalize(escalator.HandOrientation.X, escalator.HandOrientation.Y, out Point2D hand))
        {
            return hand;
        }

        return new Point2D(1d, 0d);
    }

    private bool TryCreateEntranceLine(
        Point2D endpoint,
        Point2D direction,
        double insetMeters,
        double halfWidthMeters,
        out LineString2D line)
    {
        line = null!;
        Point2D insetPoint = new(
            endpoint.X + (direction.X * insetMeters),
            endpoint.Y + (direction.Y * insetMeters));
        Point2D perpendicular = new(-direction.Y, direction.X);

        Point2D left = new(
            insetPoint.X - (perpendicular.X * halfWidthMeters),
            insetPoint.Y - (perpendicular.Y * halfWidthMeters));
        Point2D right = new(
            insetPoint.X + (perpendicular.X * halfWidthMeters),
            insetPoint.Y + (perpendicular.Y * halfWidthMeters));

        double dx = right.X - left.X;
        double dy = right.Y - left.Y;
        if (Math.Sqrt((dx * dx) + (dy * dy)) < MinOpeningLengthMeters)
        {
            return false;
        }

        line = new LineString2D(new[] { left, right });
        return true;
    }

    private void AddFeature(
        ICollection<ExportLineString> target,
        ISet<string> seenGeometryKeys,
        LineString2D lineString,
        string id,
        string category,
        string levelId,
        long elementId)
    {
        string geometryKey = BuildGeometryKey(lineString);
        if (!seenGeometryKeys.Add(geometryKey))
        {
            return;
        }

        target.Add(
            new ExportLineString(
                lineString,
                new Dictionary<string, object?>
                {
                    ["id"] = id,
                    ["category"] = category,
                    ["level_id"] = levelId,
                    ["element_id"] = elementId,
                }));
    }

    private bool IsEscalatorFamily(FamilyInstance family)
    {
        string familyName = UnitExtractor.GetFamilyName(family);
        return _zoneCatalog.TryGetFamilyInfo(familyName, out ZoneInfo info) &&
               string.Equals(info.Category, "escalator", StringComparison.OrdinalIgnoreCase);
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
            Point2D point = ProjectPoint(points3d[i]);

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

    private List<Point2D> ProjectCurveLoop(CurveLoop loop, bool closeLoop)
    {
        List<Point2D> points = new();
        foreach (Curve curve in loop)
        {
            List<Point2D> projected = ProjectCurve(curve);
            for (int i = 0; i < projected.Count; i++)
            {
                Point2D point = projected[i];
                if (points.Count == 0 || !IsSamePoint(points[points.Count - 1], point))
                {
                    points.Add(point);
                }
            }
        }

        if (closeLoop && points.Count >= 3 && !IsSamePoint(points[0], points[points.Count - 1]))
        {
            points.Add(points[0]);
        }

        return points;
    }

    private List<Point2D> ProjectCurve(Curve curve)
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

        List<Point2D> points = new(tessellated.Count);
        for (int i = 0; i < tessellated.Count; i++)
        {
            Point2D point = ProjectPoint(tessellated[i]);
            if (points.Count == 0 || !IsSamePoint(points[points.Count - 1], point))
            {
                points.Add(point);
            }
        }

        return points;
    }

    private Point2D ProjectPoint(XYZ point)
    {
        XYZ sharedPoint = _internalToSharedTransform.OfPoint(point);
        return _transformer.TransformFromRevitFeet(
            sharedPoint.X,
            sharedPoint.Y,
            offsetXMeters: 0d,
            offsetYMeters: 0d,
            rotationDegrees: 0d);
    }

    private static List<XYZ> GetBoundingBoxCorners(BoundingBoxXYZ box)
    {
        return new List<XYZ>
        {
            new(box.Min.X, box.Min.Y, box.Min.Z),
            new(box.Max.X, box.Min.Y, box.Min.Z),
            new(box.Max.X, box.Max.Y, box.Min.Z),
            new(box.Min.X, box.Max.Y, box.Min.Z),
            new(box.Min.X, box.Min.Y, box.Max.Z),
            new(box.Max.X, box.Min.Y, box.Max.Z),
            new(box.Max.X, box.Max.Y, box.Max.Z),
            new(box.Min.X, box.Max.Y, box.Max.Z),
        };
    }

    private static string BuildGeometryKey(LineString2D line)
    {
        Point2D start = line.Points[0];
        Point2D end = line.Points[line.Points.Count - 1];
        string a = $"{Math.Round(start.X, 4)}:{Math.Round(start.Y, 4)}";
        string b = $"{Math.Round(end.X, 4)}:{Math.Round(end.Y, 4)}";
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
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

    private static bool TryNormalize(double x, double y, out Point2D normalized)
    {
        normalized = default;
        double length = Math.Sqrt((x * x) + (y * y));
        if (length <= 1e-9d)
        {
            return false;
        }

        normalized = new Point2D(x / length, y / length);
        return true;
    }

    private static bool IsSamePoint(Point2D left, Point2D right)
    {
        return Math.Abs(left.X - right.X) <= 1e-8d &&
               Math.Abs(left.Y - right.Y) <= 1e-8d;
    }

    private static double Distance(Point2D a, Point2D b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static Point2D ProjectPointOntoSegment(
        Point2D point,
        Point2D start,
        Point2D end,
        out double distanceFromStart)
    {
        distanceFromStart = 0d;
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double lenSquared = (dx * dx) + (dy * dy);
        if (lenSquared <= 1e-9d)
        {
            return start;
        }

        double t = (((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / lenSquared;
        t = Math.Max(0d, Math.Min(1d, t));
        distanceFromStart = Math.Sqrt(lenSquared) * t;
        return new Point2D(start.X + (dx * t), start.Y + (dy * t));
    }

    private static bool TryGetCategory(ExportPolygon feature, out string category)
    {
        category = string.Empty;
        if (feature?.Attributes == null)
        {
            return false;
        }

        if (!feature.Attributes.TryGetValue("category", out object? value))
        {
            return false;
        }

        category = value?.ToString()?.Trim() ?? string.Empty;
        return category.Length > 0;
    }

    private readonly struct BoundarySegment
    {
        public BoundarySegment(Point2D start, Point2D end)
        {
            Start = start;
            End = end;
        }

        public Point2D Start { get; }

        public Point2D End { get; }
    }
}
