using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Schema;
using RevitGeoExporter.Export;

namespace RevitGeoExporter.Extractors;

public sealed class OpeningExtractor
{
    private const double FeetToMeters = CrsTransformer.FeetToMetersFactor;
    private const double EndpointInsetMeters = 0.05d;
    private const double MaxEscalatorWidthMeters = 1.50d;
    private const double StairLevelElevationToleranceFeet = 0.75d;
    private static readonly string[] EscalatorWidthParameterNames = { "Width", "幅" };

    private readonly Document _document;
    private readonly SharedCoordinateProjector _sharedCoordinateProjector;
    private readonly IExportMetadataProvider _metadataProvider;
    private readonly ZoneCatalog _zoneCatalog;
    private readonly GeometryRepairOptions _geometryRepairOptions;
    private readonly ExportSourceDescriptor _sourceDescriptor;
    private readonly SchemaProfile _schemaProfile;

    public OpeningExtractor(
        Document document,
        IExportMetadataProvider metadataProvider,
        ZoneCatalog zoneCatalog,
        GeometryRepairOptions? geometryRepairOptions = null,
        ExportSourceDescriptor? sourceDescriptor = null,
        SchemaProfile? schemaProfile = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _metadataProvider = metadataProvider ?? throw new ArgumentNullException(nameof(metadataProvider));
        _zoneCatalog = zoneCatalog ?? throw new ArgumentNullException(nameof(zoneCatalog));
        _sourceDescriptor = sourceDescriptor ?? ExportSourceDescriptor.CreateHost(_document);
        _sharedCoordinateProjector = new SharedCoordinateProjector(_sourceDescriptor.ProjectionProjectLocation);
        _geometryRepairOptions = (geometryRepairOptions ?? new GeometryRepairOptions()).GetEffectiveOptions();
        _schemaProfile = schemaProfile?.Clone() ?? SchemaProfile.CreateCoreProfile();
    }

    public IReadOnlyList<ExportLineString> ExtractForLevel(
        Level level,
        string levelId,
        IReadOnlyList<FamilyInstance> openingInstances,
        IReadOnlyList<ExportPolygon> unitFeatures,
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

        if (openingInstances is null)
        {
            throw new ArgumentNullException(nameof(openingInstances));
        }

        if (unitFeatures is null)
        {
            throw new ArgumentNullException(nameof(unitFeatures));
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
        HashSet<string> seenGeometryKeys = new(StringComparer.Ordinal);
        List<BoundarySegment> snapSegments = BuildSnapSegments(unitFeatures);

        foreach (FamilyInstance opening in openingInstances)
        {
            if (!skipLevelFilter && !IsOnLevel(opening, level))
            {
                continue;
            }

            if (!TryExtractOpeningLine(opening, out LineString2D lineString))
            {
                warnings.Add($"Opening {opening.Id.Value} geometry could not be extracted.");
                continue;
            }

            double maxSnapDistance = GetSnapDistance(opening, lineString, snapSegments);
            SnapResult snapResult = SnapToClosestOutline(lineString, snapSegments, maxSnapDistance);
            lineString = snapResult.Line;
            bool isElevatorDoor = OpeningFamilyClassifier.IsAcceptedElevatorDoorFamily(opening);
            if (GetLineLength(lineString) < _geometryRepairOptions.MinimumOpeningLengthMeters)
            {
                geometryRepair.DroppedOpenings++;
                warnings.Add($"Opening {opening.Id.Value} was dropped because its final length was below the configured minimum.");
                continue;
            }

            ExportElementMetadata metadata = _metadataProvider.GetElementMetadata(opening, warnings);
            AddFeature(
                features,
                seenGeometryKeys,
                opening,
                lineString,
                snapResult.WasSnapped,
                isElevatorDoor,
                metadata,
                GetOpeningCategory(opening),
                levelId,
                OpeningFamilyClassifier.GetFamilyName(opening),
                viewName,
                warnings);
        }

        return features;
    }





    private static List<BoundarySegment> BuildSnapSegments(IReadOnlyList<ExportPolygon> unitFeatures)
    {
        List<BoundarySegment> segments = new();
        foreach (ExportPolygon feature in unitFeatures)
        {
            string category = TryGetCategory(feature, out string resolvedCategory)
                ? resolvedCategory
                : string.Empty;
            foreach (Polygon2D polygon in feature.Polygons)
            {
                AddRingSegments(segments, polygon.ExteriorRing, category);
                for (int i = 0; i < polygon.InteriorRings.Count; i++)
                {
                    AddRingSegments(segments, polygon.InteriorRings[i], category);
                }
            }
        }

        return segments;
    }

    private static void AddRingSegments(
        ICollection<BoundarySegment> segments,
        IReadOnlyList<Point2D> ring,
        string category)
    {
        if (ring == null || ring.Count < 2)
        {
            return;
        }

        for (int i = 0; i < ring.Count - 1; i++)
        {
            Point2D a = ring[i];
            Point2D b = ring[i + 1];
            if (Distance(a, b) < 0.01d)
            {
                continue;
            }

            segments.Add(new BoundarySegment(a, b, category));
        }
    }

    private double GetSnapDistance(
        FamilyInstance opening,
        LineString2D line,
        IReadOnlyList<BoundarySegment> segments)
    {
        bool isNearElevatorBoundary = HasNearbyCategory(
            line,
            segments,
            "elevator",
            _geometryRepairOptions.ElevatorOpeningSnapDistanceMeters);
        return OpeningSnapPolicy.ResolveMaxSnapDistance(
            OpeningFamilyClassifier.IsAcceptedElevatorDoorFamily(opening),
            isNearElevatorBoundary,
            _geometryRepairOptions.OpeningSnapDistanceMeters,
            _geometryRepairOptions.ElevatorOpeningSnapDistanceMeters);
    }

    private static bool HasNearbyCategory(
        LineString2D line,
        IReadOnlyList<BoundarySegment> segments,
        string category,
        double maxDistance)
    {
        if (segments == null || segments.Count == 0 || line.Points.Count < 2)
        {
            return false;
        }

        Point2D start = line.Points[0];
        Point2D end = line.Points[line.Points.Count - 1];
        Point2D center = new((start.X + end.X) * 0.5d, (start.Y + end.Y) * 0.5d);

        for (int i = 0; i < segments.Count; i++)
        {
            BoundarySegment segment = segments[i];
            if (!string.Equals(segment.Category, category, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Point2D projected = ProjectPointOntoSegment(center, segment.Start, segment.End, out _);
            if (Distance(center, projected) <= maxDistance)
            {
                return true;
            }
        }

        return false;
    }

    private static SnapResult SnapToClosestOutline(LineString2D line, IReadOnlyList<BoundarySegment> segments)
    {
        return SnapToClosestOutline(line, segments, 5.0d);
    }

    private static SnapResult SnapToClosestOutline(
        LineString2D line, IReadOnlyList<BoundarySegment> segments, double maxDistance)
    {
        if (segments == null || segments.Count == 0 || line.Points.Count < 2)
        {
            return new SnapResult(line, false);
        }

        Point2D start = line.Points[0];
        Point2D end = line.Points[line.Points.Count - 1];
        Point2D center = new((start.X + end.X) * 0.5d, (start.Y + end.Y) * 0.5d);
        double length = Distance(start, end);
        if (length < 0.01d)
        {
            return new SnapResult(line, false);
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
            return new SnapResult(line, false);
        }

        BoundarySegment segment = nearest.Value;
        if (!TryNormalize(segment.End.X - segment.Start.X, segment.End.Y - segment.Start.Y, out Point2D dir))
        {
            return new SnapResult(line, false);
        }

        double segmentLength = Distance(segment.Start, segment.End);
        double halfLength = Math.Min(length * 0.5d, (segmentLength * 0.5d) - 0.01d);
        double distanceToStart = projectedT;
        double distanceToEnd = segmentLength - projectedT;
        halfLength = Math.Min(halfLength, Math.Min(distanceToStart, distanceToEnd));
        if (halfLength < 0.005d)
        {
            return new SnapResult(line, false);
        }

        Point2D snappedStart = new(projected.X - (dir.X * halfLength), projected.Y - (dir.Y * halfLength));
        Point2D snappedEnd = new(projected.X + (dir.X * halfLength), projected.Y + (dir.Y * halfLength));
        return new SnapResult(new LineString2D(new[] { snappedStart, snappedEnd }), true);
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

        Point2D axis = GetEscalatorAxis(escalator);
        BoundingBoxXYZ? box = escalator.get_BoundingBox(null);
        if (!TryGetEscalatorCenter(escalator, box, out Point2D center))
        {
            return false;
        }

        double? explicitLengthMeters = null;
        if (TryGetEscalatorCurveEndpoints(escalator, out Point2D curveStart, out Point2D curveEnd))
        {
            center = Midpoint(curveStart, curveEnd);
            explicitLengthMeters = Distance(curveStart, curveEnd);
        }

        List<Point2D>? geometryPoints = null;
        if (TryExtractElementFootprintPoints(escalator, out List<Point2D> extractedPoints))
        {
            geometryPoints = extractedPoints;
        }

        double? explicitWidthMeters = TryGetWidthFeetFromParameter(escalator);
        if (explicitWidthMeters.HasValue)
        {
            explicitWidthMeters *= FeetToMeters;
        }

        List<Point2D>? boundingBoxPoints = box == null
            ? null
            : GetBoundingBoxCorners(box).Select(ProjectPoint).ToList();

        if (!EscalatorFootprintBuilder.TryCreate(
                center,
                axis,
                geometryPoints,
                explicitLengthMeters,
                explicitWidthMeters,
                boundingBoxPoints,
                minLengthMeters: 0.05d,
                minWidthMeters: 0.05d,
                out EscalatorFootprintProjection footprint))
        {
            return false;
        }

        footprint = footprint.ClampWidth(MaxEscalatorWidthMeters);
        direction = footprint.Axis;
        start = footprint.Start;
        end = footprint.End;
        halfWidthMeters = Math.Max(0.35d, footprint.WidthMeters * 0.5d);
        return true;
    }

    private bool TryGetEscalatorCurveEndpoints(
        FamilyInstance escalator,
        out Point2D start,
        out Point2D end)
    {
        start = default;
        end = default;

        if (escalator.Location is not LocationCurve locationCurve || locationCurve.Curve == null)
        {
            return false;
        }

        List<Point2D> points = ProjectCurve(locationCurve.Curve);
        if (points.Count < 2)
        {
            return false;
        }

        start = points[0];
        end = points[points.Count - 1];
        return true;
    }

    private Point2D GetEscalatorAxis(FamilyInstance escalator)
    {
        Point2D facing = ProjectVector(escalator.FacingOrientation);
        if (TryNormalize(facing.X, facing.Y, out Point2D normalizedFacing))
        {
            return normalizedFacing;
        }

        Point2D hand = ProjectVector(escalator.HandOrientation);
        if (TryNormalize(hand.X, hand.Y, out Point2D normalizedHand))
        {
            return normalizedHand;
        }

        return new Point2D(1d, 0d);
    }

    private bool TryGetEscalatorCenter(
        FamilyInstance escalator,
        BoundingBoxXYZ? box,
        out Point2D center)
    {
        if (escalator.Location is LocationPoint locationPoint)
        {
            center = ProjectPoint(locationPoint.Point);
            return true;
        }

        if (TryGetEscalatorCurveEndpoints(escalator, out Point2D start, out Point2D end))
        {
            center = Midpoint(start, end);
            return true;
        }

        if (box == null)
        {
            center = default;
            return false;
        }

        XYZ center3d = new(
            (box.Min.X + box.Max.X) * 0.5d,
            (box.Min.Y + box.Max.Y) * 0.5d,
            (box.Min.Z + box.Max.Z) * 0.5d);
        center = ProjectPoint(center3d);
        return true;
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
        if (Math.Sqrt((dx * dx) + (dy * dy)) < _geometryRepairOptions.MinimumOpeningLengthMeters)
        {
            return false;
        }

        line = new LineString2D(new[] { left, right });
        return true;
    }

    private void AddFeature(
        ICollection<ExportLineString> target,
        ISet<string> seenGeometryKeys,
        FamilyInstance sourceElement,
        LineString2D lineString,
        bool wasSnappedToOutline,
        bool isElevatorDoor,
        ExportElementMetadata metadata,
        string category,
        string levelId,
        string familyName,
        string? viewName,
        ICollection<string> warnings)
    {
        string geometryKey = BuildGeometryKey(lineString);
        if (!seenGeometryKeys.Add(geometryKey))
        {
            return;
        }

        Dictionary<string, object?> attributes = new()
        {
            ["id"] = metadata.ExportId,
            ["category"] = category,
            ["level_id"] = levelId,
            ["element_id"] = sourceElement.Id.Value,
            ["is_snapped_to_outline"] = wasSnappedToOutline,
            ["is_elevator_door"] = isElevatorDoor,
            ["source_label"] = familyName,
            ["source_document_key"] = metadata.SourceDocumentKey,
            ["source_document_name"] = metadata.SourceDocumentName,
            ["has_persisted_export_id"] = metadata.HasPersistedId,
            ["is_linked_source"] = _sourceDescriptor.IsLinkedSource,
            ["source_link_instance_id"] = _sourceDescriptor.LinkInstanceId,
            ["source_link_instance_name"] = _sourceDescriptor.LinkInstanceName,
        };
        SchemaAttributeMapper.ApplyMappings(
            _schemaProfile,
            SchemaLayerType.Opening,
            attributes,
            sourceElement,
            viewName,
            warnings);
        target.Add(new ExportLineString(lineString, attributes));
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
        XYZ hostPoint = _sourceDescriptor.TransformToHost.OfPoint(point);
        return _sharedCoordinateProjector.ProjectPoint(hostPoint);
    }

    private Point2D ProjectVector(XYZ vector)
    {
        XYZ hostVector = _sourceDescriptor.TransformToHost.OfVector(vector);
        return _sharedCoordinateProjector.ProjectVector(hostVector);
    }

    private bool TryExtractElementFootprintPoints(Element element, out List<Point2D> points)
    {
        points = null!;

        List<List<XYZ>> loops = ExtractLoopsFromSolidGeometry(element, includeNonVisibleObjects: false);
        if (loops.Count == 0)
        {
            loops = ExtractLoopsFromSolidGeometry(element, includeNonVisibleObjects: true);
        }

        if (loops.Count == 0)
        {
            return false;
        }

        points = new List<Point2D>();
        for (int i = 0; i < loops.Count; i++)
        {
            AddProjectedLoopPoints(points, loops[i]);
        }

        return points.Count > 0;
    }

    private void AddProjectedLoopPoints(ICollection<Point2D> points, IReadOnlyList<XYZ> loop)
    {
        for (int i = 0; i < loop.Count; i++)
        {
            Point2D point = ProjectPoint(loop[i]);
            points.Add(point);
        }
    }

    private static List<List<XYZ>> ExtractLoopsFromSolidGeometry(
        Element element,
        bool includeNonVisibleObjects)
    {
        Options options = new()
        {
            ComputeReferences = false,
            DetailLevel = ViewDetailLevel.Fine,
            IncludeNonVisibleObjects = includeNonVisibleObjects,
        };

        GeometryElement? geometry = element.get_Geometry(options);
        if (geometry == null)
        {
            return new List<List<XYZ>>();
        }

        List<Solid> solids = CollectSolids(geometry);
        if (solids.Count == 0)
        {
            return new List<List<XYZ>>();
        }

        PlanarFace? lowestFace = null;
        double lowestZ = double.MaxValue;
        for (int i = 0; i < solids.Count; i++)
        {
            Solid solid = solids[i];
            if (solid.Volume <= 0d)
            {
                continue;
            }

            foreach (Face face in solid.Faces)
            {
                if (face is not PlanarFace planarFace)
                {
                    continue;
                }

                if (planarFace.FaceNormal.Z >= -0.9d)
                {
                    continue;
                }

                if (planarFace.Origin.Z < lowestZ)
                {
                    lowestFace = planarFace;
                    lowestZ = planarFace.Origin.Z;
                }
            }
        }

        return lowestFace == null ? new List<List<XYZ>>() : ExtractLoopsFromFace(lowestFace);
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

    private static List<List<XYZ>> ExtractLoopsFromFace(Face face)
    {
        List<List<XYZ>> loops = new();
        foreach (EdgeArray edgeArray in face.EdgeLoops)
        {
            List<XYZ> loop = new();
            foreach (Edge edge in edgeArray)
            {
                IList<XYZ> tessellated = edge.AsCurve().Tessellate();
                for (int i = 0; i < tessellated.Count; i++)
                {
                    if (loop.Count == 0 || !loop[loop.Count - 1].IsAlmostEqualTo(tessellated[i]))
                    {
                        loop.Add(tessellated[i]);
                    }
                }
            }

            if (loop.Count >= 3 && !loop[0].IsAlmostEqualTo(loop[loop.Count - 1]))
            {
                loop.Add(loop[0]);
            }

            if (loop.Count >= 4)
            {
                loops.Add(loop);
            }
        }

        return loops;
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
        double? width = TryGetWidthFeetFromParameter(opening);
        if (width.HasValue && width.Value > 1e-6d)
        {
            return width.Value;
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

    private static double? TryGetWidthFeetFromParameter(FamilyInstance opening)
    {
        double width = TryReadWidthFromElement(opening);
        if (width > 1e-6d)
        {
            return width;
        }

        if (opening.Symbol == null)
        {
            return null;
        }

        width = TryReadWidthFromElement(opening.Symbol);
        return width > 1e-6d ? width : null;
    }

    private static double TryReadWidth(Parameter? parameter)
    {
        if (parameter == null || parameter.StorageType != StorageType.Double)
        {
            return 0d;
        }

        return parameter.AsDouble();
    }

    private static double TryReadWidthFromElement(Element element)
    {
        for (int i = 0; i < EscalatorWidthParameterNames.Length; i++)
        {
            double width = TryReadWidth(element.LookupParameter(EscalatorWidthParameterNames[i]));
            if (width > 1e-6d)
            {
                return width;
            }
        }

        return 0d;
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
        string familyName = OpeningFamilyClassifier.GetFamilyName(opening);
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

    private static Point2D Midpoint(Point2D a, Point2D b)
    {
        return new Point2D((a.X + b.X) * 0.5d, (a.Y + b.Y) * 0.5d);
    }

    private static double Distance(Point2D a, Point2D b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double GetLineLength(LineString2D line)
    {
        double total = 0d;
        for (int i = 0; i < line.Points.Count - 1; i++)
        {
            total += Distance(line.Points[i], line.Points[i + 1]);
        }

        return total;
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
        public BoundarySegment(Point2D start, Point2D end, string category)
        {
            Start = start;
            End = end;
            Category = category ?? string.Empty;
        }

        public Point2D Start { get; }

        public Point2D End { get; }

        public string Category { get; }
    }

    private readonly struct SnapResult
    {
        public SnapResult(LineString2D line, bool wasSnapped)
        {
            Line = line;
            WasSnapped = wasSnapped;
        }

        public LineString2D Line { get; }

        public bool WasSnapped { get; }
    }
}

