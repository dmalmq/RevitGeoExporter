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
using RevitGeoExporter.Core.Schema;
using RevitGeoExporter.Core.Utilities;
using RevitGeoExporter.Export;

namespace RevitGeoExporter.Extractors;

public sealed class DetailExtractor
{
    private const double FeetToMeters = CrsTransformer.FeetToMetersFactor;
    private const double StairStepFallbackSpacingMeters = 0.30d;
    private static readonly GeometryFactory GeometryFactory = new();

    private readonly Document _document;
    private readonly SharedCoordinateProjector _sharedCoordinateProjector;
    private readonly GeometryRepairOptions _geometryRepairOptions;
    private readonly ExportSourceDescriptor _sourceDescriptor;
    private readonly string _sourceDocumentKey;
    private readonly string _sourceDocumentName;
    private readonly SchemaProfile _schemaProfile;

    public DetailExtractor(
        Document document,
        GeometryRepairOptions? geometryRepairOptions = null,
        ExportSourceDescriptor? sourceDescriptor = null,
        SchemaProfile? schemaProfile = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _sourceDescriptor = sourceDescriptor ?? ExportSourceDescriptor.CreateHost(_document);
        _sharedCoordinateProjector = new SharedCoordinateProjector(_sourceDescriptor.ProjectionProjectLocation);
        _geometryRepairOptions = (geometryRepairOptions ?? new GeometryRepairOptions()).GetEffectiveOptions();
        _sourceDocumentKey = DocumentProjectKeyBuilder.Create(_document);
        _sourceDocumentName = DocumentProjectKeyBuilder.CreateDisplayName(_document);
        _schemaProfile = schemaProfile?.Clone() ?? SchemaProfile.CreateCoreProfile();
    }

    public IReadOnlyList<ExportLineString> ExtractForLevel(
        Level level,
        string levelId,
        IReadOnlyList<CurveElement> detailCurves,
        IReadOnlyList<Stairs> stairs,
        GeometryRepairResult geometryRepair,
        ICollection<string> warnings,
        string? viewName = null,
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
                    curveElement,
                    lineString,
                    BuildSyntheticId("detail", curveElement.Id.Value, levelId),
                    levelId,
                    "detail-curve",
                    viewName,
                    warnings));
        }

        features.AddRange(ExtractStairStepLines(stairs, levelId, geometryRepair, warnings, viewName));
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
        ICollection<string> warnings,
        string? viewName)
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

                if (!TryBuildRunStepLines(stair, run, out List<LineString2D> stepLines, geometryRepair, warnings))
                {
                    continue;
                }

                for (int i = 0; i < stepLines.Count; i++)
                {
                    long syntheticElementId = (run.Id.Value * 1000L) + (i + 1);
                    features.Add(
                        CreateFeature(
                            run,
                            stepLines[i],
                            BuildSyntheticId("detail.stair.step", syntheticElementId, levelId),
                            levelId,
                            "stair-step",
                            viewName,
                            warnings));
                }
            }
        }

        return features;
    }

    private bool TryBuildRunStepLines(
        Stairs stair,
        StairsRun run,
        out List<LineString2D> lines,
        GeometryRepairResult geometryRepair,
        ICollection<string> warnings)
    {
        lines = new List<LineString2D>();

        CurveLoop stairsPath;
        try
        {
            stairsPath = run.GetStairsPath();
        }
        catch (Exception)
        {
            warnings.Add($"{BuildStairRunWarningPrefix(stair, run)} path could not be read for detail export.");
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

        if (TryBuildModeledRunStepLines(stair, run, pathPoints, pathLengthMeters, warnings, out lines))
        {
            return true;
        }

        CurveLoop footprintBoundary;
        try
        {
            footprintBoundary = run.GetFootprintBoundary();
        }
        catch (Exception)
        {
            warnings.Add($"{BuildStairRunWarningPrefix(stair, run)} footprint boundary could not be read for detail export.");
            return false;
        }

        if (!TryCreatePolygonFromCurveLoop(
                footprintBoundary,
                out Polygon footprintPolygon,
                warnings,
                $"{BuildStairRunWarningPrefix(stair, run)} footprint boundary"))
        {
            return false;
        }

        if (TryBuildSchematicRunStepLines(run, pathPoints, pathLengthMeters, footprintPolygon, geometryRepair, out lines))
        {
            geometryRepair.SimplifiedDetails++;
            return true;
        }

        return false;
    }

    private bool TryBuildModeledRunStepLines(
        Stairs stair,
        StairsRun run,
        IReadOnlyList<Point2D> pathPoints,
        double pathLengthMeters,
        ICollection<string> warnings,
        out List<LineString2D> lines)
    {
        lines = new List<LineString2D>();

        GeometryElement? geometry;
        try
        {
            geometry = run.get_Geometry(CreateGeometryOptions());
        }
        catch (Exception)
        {
            warnings.Add($"{BuildStairRunWarningPrefix(stair, run)} tread geometry could not be read; using schematic stair detail lines.");
            return false;
        }

        if (geometry == null)
        {
            return false;
        }

        List<Solid> solids = CollectSolids(geometry);
        if (solids.Count == 0)
        {
            return false;
        }

        double runWidthMeters = Math.Max(0.5d, run.ActualRunWidth * FeetToMeters);
        double minLineLengthMeters = _geometryRepairOptions.MinimumOpeningLengthMeters;
        double maxDistanceToPathMeters = Math.Max(minLineLengthMeters, runWidthMeters * 0.35d);
        List<StairTreadLineCandidate> candidates = new();

        for (int solidIndex = 0; solidIndex < solids.Count; solidIndex++)
        {
            Solid solid = solids[solidIndex];
            foreach (Face face in solid.Faces)
            {
                if (face is not PlanarFace planarFace || planarFace.FaceNormal.Z < 0.98d)
                {
                    continue;
                }

                if (TryBuildTreadFaceLineCandidate(
                        planarFace,
                        pathPoints,
                        minLineLengthMeters,
                        maxDistanceToPathMeters,
                        out StairTreadLineCandidate candidate))
                {
                    candidates.Add(candidate);
                }
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        IReadOnlyList<LineString2D> selected = StairTreadLineSelector.SelectDistinctOrderedLines(
            candidates,
            BuildStationMergeThreshold(run, pathLengthMeters));
        if (selected.Count == 0)
        {
            return false;
        }

        lines.AddRange(selected);
        return true;
    }

    private bool TryBuildTreadFaceLineCandidate(
        PlanarFace face,
        IReadOnlyList<Point2D> pathPoints,
        double minimumLineLengthMeters,
        double maxDistanceToPathMeters,
        out StairTreadLineCandidate candidate)
    {
        candidate = default;
        List<StairTreadLineCandidate> edgeCandidates = new();
        List<Point2D> facePoints = new();

        foreach (EdgeArray edgeArray in face.EdgeLoops)
        {
            foreach (Edge edge in edgeArray)
            {
                if (!TryExtractEdgeLineString(edge, out LineString2D line))
                {
                    continue;
                }

                if (!TryInterpolateOnPolyline(line.Points, 0.5d, out Point2D midpoint, out _))
                {
                    continue;
                }

                if (!TryLocateOnPolyline(pathPoints, midpoint, out double stationMeters, out _, out double distanceToPathMeters) ||
                    distanceToPathMeters > maxDistanceToPathMeters)
                {
                    continue;
                }

                edgeCandidates.Add(new StairTreadLineCandidate(line, stationMeters));
                facePoints.AddRange(line.Points);
            }
        }

        if (edgeCandidates.Count == 0 || facePoints.Count < 3)
        {
            return false;
        }

        Point2D centroid = GetAveragePoint(facePoints);
        if (!TryLocateOnPolyline(pathPoints, centroid, out _, out Point2D tangent, out double faceDistanceToPathMeters) ||
            faceDistanceToPathMeters > maxDistanceToPathMeters)
        {
            return false;
        }

        return StairTreadLineSelector.TrySelectFrontEdge(edgeCandidates, tangent, minimumLineLengthMeters, out candidate);
    }

    private bool TryBuildSchematicRunStepLines(
        StairsRun run,
        IReadOnlyList<Point2D> pathPoints,
        double pathLengthMeters,
        Polygon footprintPolygon,
        GeometryRepairResult geometryRepair,
        out List<LineString2D> lines)
    {
        lines = new List<LineString2D>();

        IReadOnlyList<double> linePositions = BuildActualStepPositions(run, pathLengthMeters);
        double halfCutLengthMeters = Math.Max(0.5d, run.ActualRunWidth * FeetToMeters) * 1.5d;
        HashSet<string> emittedLineKeys = new(StringComparer.Ordinal);
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

    private static double BuildStationMergeThreshold(StairsRun run, double pathLengthMeters)
    {
        int treadCount = Math.Max(run.ActualTreadsNumber, Math.Max(1, run.ActualRisersNumber - 1));
        if (treadCount <= 0 || pathLengthMeters <= 0d)
        {
            return 0.05d;
        }

        double nominalTreadDepthMeters = pathLengthMeters / treadCount;
        return Math.Max(0.03d, nominalTreadDepthMeters * 0.2d);
    }

    private static IReadOnlyList<double> BuildActualStepPositions(StairsRun run, double pathLengthMeters)
    {
        int riserCount = run.ActualRisersNumber;

        if (riserCount < 2)
        {
            return StairDetailLayout.BuildCenteredLinePositions(pathLengthMeters, StairStepFallbackSpacingMeters);
        }

        double treadDepthMeters = pathLengthMeters / (riserCount - 1);
        if (treadDepthMeters <= 1e-6d)
        {
            return StairDetailLayout.BuildCenteredLinePositions(pathLengthMeters, StairStepFallbackSpacingMeters);
        }

        List<double> positions = new(riserCount - 1);
        for (int i = 1; i < riserCount; i++)
        {
            double position = i * treadDepthMeters;
            if (position < pathLengthMeters - 1e-6d)
            {
                positions.Add(position);
            }
        }

        return positions.Count > 0
            ? positions
            : StairDetailLayout.BuildCenteredLinePositions(pathLengthMeters, StairStepFallbackSpacingMeters);
    }

    private bool TryExtractEdgeLineString(Edge edge, out LineString2D line)
    {
        line = null!;
        IList<XYZ> tessellated = edge.Tessellate();
        if (tessellated.Count < 2)
        {
            Curve? curve = edge.AsCurve();
            if (curve == null)
            {
                return false;
            }

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

        line = new LineString2D(points);
        return true;
    }

    private static Options CreateGeometryOptions()
    {
        return new Options
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = true,
            DetailLevel = ViewDetailLevel.Fine,
        };
    }

    private static List<Solid> CollectSolids(GeometryElement geometry)
    {
        List<Solid> solids = new();
        foreach (GeometryObject geometryObject in geometry)
        {
            switch (geometryObject)
            {
                case Solid solid when solid.Volume > 0d:
                    solids.Add(solid);
                    break;
                case GeometryInstance instance:
                    solids.AddRange(CollectSolids(instance.GetInstanceGeometry()));
                    break;
            }
        }

        return solids;
    }

    private bool TryCreatePolygonFromCurveLoop(
        CurveLoop loop,
        out Polygon polygon,
        ICollection<string>? warnings = null,
        string? context = null)
    {
        polygon = null!;
        List<Point2D> points = ProjectCurveLoop(loop, closeLoop: true);
        if (points.Count < 4)
        {
            if (!string.IsNullOrWhiteSpace(context))
            {
                warnings?.Add($"{context} could not form a closed polygon for detail export.");
            }

            return false;
        }

        List<Coordinate> coordinates = points
            .Select(point => new Coordinate(point.X, point.Y))
            .ToList();
        Coordinate first = coordinates[0];
        Coordinate last = coordinates[coordinates.Count - 1];
        if (!first.Equals2D(last))
        {
            coordinates.Add(new Coordinate(first.X, first.Y));
        }

        LinearRing shell;
        try
        {
            shell = GeometryFactory.CreateLinearRing(coordinates.ToArray());
        }
        catch (ArgumentException)
        {
            if (!string.IsNullOrWhiteSpace(context))
            {
                warnings?.Add($"{context} could not form a closed polygon for detail export.");
            }

            return false;
        }

        Polygon created = GeometryFactory.CreatePolygon(shell);
        if (!created.IsValid)
        {
            Geometry healed = created.Buffer(0d);
            if (healed is Polygon healedPolygon)
            {
                polygon = healedPolygon;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(context))
            {
                warnings?.Add($"{context} produced an invalid polygon for detail export.");
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
        Element sourceElement,
        LineString2D lineString,
        string id,
        string levelId,
        string sourceLabel,
        string? viewName,
        ICollection<string> warnings)
    {
        Dictionary<string, object?> attributes = new()
        {
            ["id"] = id,
            ["level_id"] = levelId,
            ["element_id"] = sourceElement.Id.Value,
            ["source_label"] = sourceLabel,
            ["source_document_key"] = _sourceDocumentKey,
            ["source_document_name"] = _sourceDocumentName,
            ["has_persisted_export_id"] = false,
            ["is_linked_source"] = _sourceDescriptor.IsLinkedSource,
            ["source_link_instance_id"] = _sourceDescriptor.LinkInstanceId,
            ["source_link_instance_name"] = _sourceDescriptor.LinkInstanceName,
        };
        SchemaAttributeMapper.ApplyMappings(
            _schemaProfile,
            SchemaLayerType.Detail,
            attributes,
            sourceElement,
            viewName,
            warnings);
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

    private static bool TryLocateOnPolyline(
        IReadOnlyList<Point2D> polyline,
        Point2D point,
        out double stationMeters,
        out Point2D tangent,
        out double distanceToPathMeters)
    {
        stationMeters = 0d;
        tangent = default;
        distanceToPathMeters = double.MaxValue;

        if (polyline == null || polyline.Count < 2)
        {
            return false;
        }

        bool found = false;
        double traversed = 0d;
        double bestDistanceSquared = double.MaxValue;
        for (int i = 0; i < polyline.Count - 1; i++)
        {
            Point2D start = polyline[i];
            Point2D end = polyline[i + 1];
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double segmentLengthSquared = (dx * dx) + (dy * dy);
            if (segmentLengthSquared <= 1e-9d)
            {
                continue;
            }

            double segmentLength = Math.Sqrt(segmentLengthSquared);
            double projection = (((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / segmentLengthSquared;
            double clampedProjection = Math.Max(0d, Math.Min(1d, projection));
            double projectedX = start.X + (dx * clampedProjection);
            double projectedY = start.Y + (dy * clampedProjection);
            double distanceX = point.X - projectedX;
            double distanceY = point.Y - projectedY;
            double distanceSquared = (distanceX * distanceX) + (distanceY * distanceY);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                stationMeters = traversed + (segmentLength * clampedProjection);
                tangent = new Point2D(dx / segmentLength, dy / segmentLength);
                distanceToPathMeters = Math.Sqrt(distanceSquared);
                found = true;
            }

            traversed += segmentLength;
        }

        return found;
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
        return ComparePoints(start, end) <= 0
            ? BuildLineKey(start, end)
            : BuildLineKey(end, start);
    }

    private static string BuildLineKey(Point2D start, Point2D end)
    {
        return $"{Math.Round(start.X, 4)}:{Math.Round(start.Y, 4)}:{Math.Round(end.X, 4)}:{Math.Round(end.Y, 4)}";
    }

    private static int ComparePoints(Point2D left, Point2D right)
    {
        int xComparison = left.X.CompareTo(right.X);
        return xComparison != 0 ? xComparison : left.Y.CompareTo(right.Y);
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

    private static Point2D GetAveragePoint(IReadOnlyList<Point2D> points)
    {
        double x = 0d;
        double y = 0d;
        for (int i = 0; i < points.Count; i++)
        {
            x += points[i].X;
            y += points[i].Y;
        }

        double scale = points.Count == 0 ? 0d : 1d / points.Count;
        return new Point2D(x * scale, y * scale);
    }

    private string BuildStairRunWarningPrefix(Stairs stair, StairsRun run)
    {
        string prefix = $"Stairs {stair.Id.Value} run {run.Id.Value}";
        return _sourceDescriptor.IsLinkedSource
            ? $"{prefix} in source '{_sourceDocumentName}'"
            : prefix;
    }

    private static bool IsSamePoint(Point2D left, Point2D right)
    {
        return Math.Abs(left.X - right.X) <= 1e-8d &&
               Math.Abs(left.Y - right.Y) <= 1e-8d;
    }
}

