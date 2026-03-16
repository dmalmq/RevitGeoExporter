using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using NetTopologySuite.Geometries;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Utilities;
using RevitGeoExporter.Export;

namespace RevitGeoExporter.Extractors;

public sealed class DetailExtractor
{
    private const double FeetToMeters = CrsTransformer.FeetToMetersFactor;
    private const double StairDetailSpacingMeters = 0.60d;
    private static readonly GeometryFactory GeometryFactory = new();

    private readonly Document _document;
    private readonly SharedCoordinateProjector _sharedCoordinateProjector;
    private readonly GeometryRepairOptions _geometryRepairOptions;
    private readonly ExportSourceDescriptor _sourceDescriptor;
    private readonly string _sourceDocumentKey;
    private readonly string _sourceDocumentName;

    public DetailExtractor(
        Document document,
        GeometryRepairOptions? geometryRepairOptions = null,
        ExportSourceDescriptor? sourceDescriptor = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _sourceDescriptor = sourceDescriptor ?? ExportSourceDescriptor.CreateHost(_document);
        _sharedCoordinateProjector = new SharedCoordinateProjector(_sourceDescriptor.ProjectionProjectLocation);
        _geometryRepairOptions = (geometryRepairOptions ?? new GeometryRepairOptions()).GetEffectiveOptions();
        _sourceDocumentKey = DocumentProjectKeyBuilder.Create(_document);
        _sourceDocumentName = DocumentProjectKeyBuilder.CreateDisplayName(_document);
    }

    public IReadOnlyList<ExportLineString> ExtractForLevel(
        Level level,
        string levelId,
        IReadOnlyList<CurveElement> detailCurves,
        IReadOnlyList<Stairs> stairs,
        GeometryRepairResult geometryRepair,
        ICollection<string> warnings,
        bool skipLevelFilter = false)
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

        if (stairs is null)
        {
            throw new ArgumentNullException(nameof(stairs));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        if (geometryRepair is null)
        {
            throw new ArgumentNullException(nameof(geometryRepair));
        }

        List<ExportLineString> features = new();
        foreach (CurveElement curveElement in detailCurves)
        {
            if (curveElement is not ModelCurve)
            {
                continue;
            }

            if (!skipLevelFilter && !IsOnLevel(curveElement, level))
            {
                continue;
            }

            if (!TryExtractLineString(curveElement, out LineString2D lineString))
            {
                warnings.Add($"Detail line {curveElement.Id.Value} geometry could not be extracted.");
                continue;
            }

            features.Add(
                CreateFeature(
                    lineString,
                    BuildSyntheticId("detail", curveElement.Id.Value, levelId),
                    levelId,
                    curveElement.Id.Value,
                    "detail-curve"));
        }

        features.AddRange(ExtractStairStepLines(stairs, levelId, geometryRepair, warnings));
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
            Point2D point = ProjectPoint(tessellated[i]);

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

    private IReadOnlyList<ExportLineString> ExtractStairStepLines(
        IReadOnlyList<Stairs> stairs,
        string levelId,
        GeometryRepairResult geometryRepair,
        ICollection<string> warnings)
    {
        List<ExportLineString> features = new();
        foreach (Stairs stair in stairs)
        {
            foreach (ElementId runId in stair.GetStairsRuns())
            {
                if (_document.GetElement(runId) is not StairsRun run)
                {
                    continue;
                }

                if (!TryBuildRunStepLines(run, out List<LineString2D> stepLines, geometryRepair, warnings))
                {
                    continue;
                }

                for (int i = 0; i < stepLines.Count; i++)
                {
                    long syntheticElementId = (run.Id.Value * 1000L) + (i + 1);
                    features.Add(
                        CreateFeature(
                            stepLines[i],
                            BuildSyntheticId("detail.stair.step", syntheticElementId, levelId),
                            levelId,
                            run.Id.Value,
                            "stair-step"));
                }
            }
        }

        return features;
    }

    private bool TryBuildRunStepLines(
        StairsRun run,
        out List<LineString2D> lines,
        GeometryRepairResult geometryRepair,
        ICollection<string> warnings)
    {
        lines = new List<LineString2D>();
        CurveLoop footprintBoundary;
        CurveLoop stairsPath;
        try
        {
            footprintBoundary = run.GetFootprintBoundary();
            stairsPath = run.GetStairsPath();
        }
        catch (Exception)
        {
            warnings.Add($"Stairs run {run.Id.Value} path/boundary could not be read for detail export.");
            return false;
        }

        if (!TryCreatePolygonFromCurveLoop(footprintBoundary, out Polygon footprintPolygon))
        {
            return false;
        }

        List<Point2D> pathPoints = ProjectCurveLoop(stairsPath, closeLoop: false);
        if (pathPoints.Count < 2)
        {
            return false;
        }

        double pathLengthMeters = GetLength(pathPoints);
        if (pathLengthMeters < _geometryRepairOptions.MinimumOpeningLengthMeters)
        {
            return false;
        }

        double runWidthMeters = Math.Max(0.5d, run.ActualRunWidth * FeetToMeters);
        double halfCutLengthMeters = runWidthMeters * 1.5d;
        HashSet<string> emittedLineKeys = new(StringComparer.Ordinal);

        IReadOnlyList<double> linePositions = StairDetailLayout.BuildCenteredLinePositions(
            pathLengthMeters,
            StairDetailSpacingMeters);
        for (int i = 0; i < linePositions.Count; i++)
        {
            double distanceMeters = linePositions[i];
            double fraction = pathLengthMeters <= 1e-9d ? 0d : distanceMeters / pathLengthMeters;
            if (!TryInterpolateOnPolyline(pathPoints, fraction, out Point2D point, out Point2D tangent))
            {
                continue;
            }

            Point2D perpendicular = new(-tangent.Y, tangent.X);
            Coordinate start = new(
                point.X - (perpendicular.X * halfCutLengthMeters),
                point.Y - (perpendicular.Y * halfCutLengthMeters));
            Coordinate end = new(
                point.X + (perpendicular.X * halfCutLengthMeters),
                point.Y + (perpendicular.Y * halfCutLengthMeters));
            LineString cutLine = GeometryFactory.CreateLineString(new[] { start, end });

            if (!TryGetLongestIntersectionSegment(footprintPolygon, cutLine, out LineString2D segment))
            {
                continue;
            }

            if (GetLength(segment.Points) < _geometryRepairOptions.MinimumOpeningLengthMeters)
            {
                geometryRepair.SimplifiedDetails++;
                continue;
            }

            string lineKey = BuildLineKey(segment);
            if (emittedLineKeys.Add(lineKey))
            {
                lines.Add(segment);
            }
        }

        return lines.Count > 0;
    }

    private bool TryCreatePolygonFromCurveLoop(CurveLoop loop, out Polygon polygon)
    {
        polygon = null!;
        List<Point2D> points = ProjectCurveLoop(loop, closeLoop: true);
        if (points.Count < 4)
        {
            return false;
        }

        Coordinate[] coordinates = points
            .Select(point => new Coordinate(point.X, point.Y))
            .ToArray();
        LinearRing shell = GeometryFactory.CreateLinearRing(coordinates);
        Polygon created = GeometryFactory.CreatePolygon(shell);
        if (!created.IsValid)
        {
            Geometry healed = created.Buffer(0d);
            if (healed is Polygon healedPolygon)
            {
                polygon = healedPolygon;
                return true;
            }

            return false;
        }

        polygon = created;
        return true;
    }

    private List<Point2D> ProjectCurveLoop(CurveLoop loop, bool closeLoop)
    {
        List<Point2D> points = new();
        foreach (Curve curve in loop)
        {
            List<Point2D> curvePoints = ProjectCurve(curve);
            for (int i = 0; i < curvePoints.Count; i++)
            {
                Point2D point = curvePoints[i];
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
        XYZ hostPoint = _sourceDescriptor.TransformToHost.OfPoint(point);
        return _sharedCoordinateProjector.ProjectPoint(hostPoint);
    }

    private ExportLineString CreateFeature(
        LineString2D lineString,
        string id,
        string levelId,
        long elementId,
        string sourceLabel)
    {
        Dictionary<string, object?> attributes = new()
        {
            ["id"] = id,
            ["level_id"] = levelId,
            ["element_id"] = elementId,
            ["source_label"] = sourceLabel,
            ["source_document_key"] = _sourceDocumentKey,
            ["source_document_name"] = _sourceDocumentName,
            ["is_linked_source"] = _sourceDescriptor.IsLinkedSource,
            ["source_link_instance_id"] = _sourceDescriptor.LinkInstanceId,
            ["source_link_instance_name"] = _sourceDescriptor.LinkInstanceName,
        };
        return new ExportLineString(lineString, attributes);
    }

    private string BuildSyntheticId(string scope, long elementId, string levelId)
    {
        if (!_sourceDescriptor.IsLinkedSource || !_sourceDescriptor.LinkInstanceId.HasValue)
        {
            return StableIdGenerator.Create(scope, elementId, levelId);
        }

        return DeterministicIdGenerator.CreateGuid(
            scope,
            _sourceDocumentKey,
            _sourceDescriptor.LinkInstanceId.Value.ToString(),
            elementId.ToString(),
            levelId);
    }

    private static bool TryInterpolateOnPolyline(
        IReadOnlyList<Point2D> points,
        double fraction,
        out Point2D position,
        out Point2D tangent)
    {
        position = default;
        tangent = default;
        if (points == null || points.Count < 2)
        {
            return false;
        }

        double clampedFraction = Math.Max(0d, Math.Min(1d, fraction));
        double totalLength = GetLength(points);
        if (totalLength <= 1e-9d)
        {
            return false;
        }

        double targetDistance = totalLength * clampedFraction;
        double traversed = 0d;
        for (int i = 0; i < points.Count - 1; i++)
        {
            Point2D a = points[i];
            Point2D b = points[i + 1];
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length <= 1e-9d)
            {
                continue;
            }

            if (traversed + length >= targetDistance)
            {
                double segmentDistance = targetDistance - traversed;
                double t = segmentDistance / length;
                position = new Point2D(a.X + (dx * t), a.Y + (dy * t));
                tangent = new Point2D(dx / length, dy / length);
                return true;
            }

            traversed += length;
        }

        Point2D lastStart = points[points.Count - 2];
        Point2D lastEnd = points[points.Count - 1];
        double lastDx = lastEnd.X - lastStart.X;
        double lastDy = lastEnd.Y - lastStart.Y;
        double lastLength = Math.Sqrt((lastDx * lastDx) + (lastDy * lastDy));
        if (lastLength <= 1e-9d)
        {
            return false;
        }

        position = lastEnd;
        tangent = new Point2D(lastDx / lastLength, lastDy / lastLength);
        return true;
    }

    private static bool TryGetLongestIntersectionSegment(
        Polygon polygon,
        LineString cutLine,
        out LineString2D segment)
    {
        segment = null!;
        Geometry intersection = polygon.Intersection(cutLine);
        if (intersection.IsEmpty)
        {
            return false;
        }

        List<LineString> lineStrings = new();
        CollectLineStrings(intersection, lineStrings);
        if (lineStrings.Count == 0)
        {
            return false;
        }

        LineString longest = lineStrings
            .OrderByDescending(line => line.Length)
            .First();
        if (longest.Length <= 1e-9d)
        {
            return false;
        }

        List<Point2D> points = longest.Coordinates
            .Select(coord => new Point2D(coord.X, coord.Y))
            .ToList();
        if (points.Count < 2)
        {
            return false;
        }

        segment = new LineString2D(points);
        return true;
    }

    private static void CollectLineStrings(Geometry geometry, ICollection<LineString> target)
    {
        switch (geometry)
        {
            case LineString lineString:
                target.Add(lineString);
                break;
            case MultiLineString multiLineString:
                for (int i = 0; i < multiLineString.NumGeometries; i++)
                {
                    if (multiLineString.GetGeometryN(i) is LineString childLine)
                    {
                        target.Add(childLine);
                    }
                }

                break;
            case GeometryCollection collection:
                for (int i = 0; i < collection.NumGeometries; i++)
                {
                    CollectLineStrings(collection.GetGeometryN(i), target);
                }

                break;
        }
    }

    private static string BuildLineKey(LineString2D line)
    {
        Point2D start = line.Points[0];
        Point2D end = line.Points[line.Points.Count - 1];
        return $"{Math.Round(start.X, 4)}:{Math.Round(start.Y, 4)}:{Math.Round(end.X, 4)}:{Math.Round(end.Y, 4)}";
    }

    private static double GetLength(IReadOnlyList<Point2D> points)
    {
        double total = 0d;
        for (int i = 0; i < points.Count - 1; i++)
        {
            double dx = points[i + 1].X - points[i].X;
            double dy = points[i + 1].Y - points[i].Y;
            total += Math.Sqrt((dx * dx) + (dy * dy));
        }

        return total;
    }

    private static bool IsSamePoint(Point2D left, Point2D right)
    {
        return Math.Abs(left.X - right.X) <= 1e-8d &&
               Math.Abs(left.Y - right.Y) <= 1e-8d;
    }
}

