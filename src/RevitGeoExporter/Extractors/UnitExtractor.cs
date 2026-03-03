using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Precision;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Extractors;

public sealed class UnitExtractor
{
    private const double MinSplitAreaSquareMeters = 0.05d;

    private static readonly GeometryFactory GeometryFactory =
        new(new PrecisionModel(1_000_000d));

    private readonly Document _document;
    private readonly Transform _internalToSharedTransform;
    private readonly CrsTransformer _transformer;
    private readonly ZoneCatalog _zoneCatalog;
    private readonly SharedParameterManager _parameterManager;
    private readonly string _source;

    public UnitExtractor(
        Document document,
        ZoneCatalog zoneCatalog,
        SharedParameterManager parameterManager,
        string source)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _internalToSharedTransform =
            _document.ActiveProjectLocation?.GetTotalTransform() ?? Transform.Identity;
        _transformer = new CrsTransformer();
        _zoneCatalog = zoneCatalog ?? throw new ArgumentNullException(nameof(zoneCatalog));
        _parameterManager = parameterManager ?? throw new ArgumentNullException(nameof(parameterManager));
        _source = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim();
    }

    public sealed class FloorSplitMask
    {
        internal FloorSplitMask(Geometry geometry)
        {
            Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        }

        internal Geometry Geometry { get; }
    }

    public FloorSplitMask? CreateFloorSplitMask(IReadOnlyList<Wall> walls, ICollection<string> warnings)
    {
        if (walls is null)
        {
            throw new ArgumentNullException(nameof(walls));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        if (walls.Count == 0)
        {
            return null;
        }

        List<Geometry> wallGeometries = new();
        foreach (Wall wall in walls)
        {
            Geometry? wallGeometry = TryProjectWallCenterline(wall, warnings);
            if (wallGeometry != null && !wallGeometry.IsEmpty)
            {
                wallGeometries.Add(wallGeometry);
            }
        }

        if (wallGeometries.Count == 0)
        {
            return null;
        }

        Geometry unioned = UnaryUnionOp.Union(wallGeometries);
        if (unioned.IsEmpty)
        {
            return null;
        }

        return new FloorSplitMask(unioned);
    }

    public bool TryCreateFloorUnits(
        Floor floor,
        string levelId,
        FloorSplitMask? splitMask,
        ICollection<string> warnings,
        out IReadOnlyList<ExportPolygon> features)
    {
        features = Array.Empty<ExportPolygon>();
        if (floor is null)
        {
            return false;
        }

        long elementId = floor.Id.Value;
        if (!TryExtractElementPolygon(floor, out Polygon2D polygon))
        {
            warnings.Add($"Floor {elementId} geometry could not be extracted.");
            return false;
        }

        string typeName = GetElementTypeName(floor);
        ZoneNameParseResult parsed = ZoneNameParser.Parse(typeName);
        string zoneName = parsed.PatternMatched ? parsed.ZoneName : typeName;
        if (!parsed.PatternMatched)
        {
            warnings.Add($"Floor {elementId} type '{typeName}' did not match expected pattern.");
        }

        bool foundZone = _zoneCatalog.TryGetZoneInfo(zoneName, out ZoneInfo zoneInfo);
        if (!foundZone)
        {
            warnings.Add(
                $"Floor {elementId} zone '{zoneName}' was not found in catalog. Default category/restriction applied.");
        }

        string baseId = _parameterManager.GetOrCreateElementId(floor, warnings);
        string? name = _parameterManager.GetOptionalStringParameter(floor, SharedParameterManager.ImdfNameParameterName);
        string? altName = _parameterManager.GetOptionalStringParameter(floor, SharedParameterManager.ImdfAltNameParameterName);

        List<Polygon2D> splitPolygons = splitMask == null
            ? new List<Polygon2D>()
            : SplitPolygonByWalls(polygon, splitMask.Geometry);

        if (splitPolygons.Count <= 1)
        {
            Polygon2D singlePolygon = splitPolygons.Count == 1 ? splitPolygons[0] : polygon;
            features = new[]
            {
                CreateFeature(
                    baseId,
                    singlePolygon,
                    levelId,
                    zoneInfo,
                    name,
                    altName),
            };
            return true;
        }

        List<(Polygon2D Polygon, Point2D Centroid)> orderedParts = splitPolygons
            .Select(p => (Polygon: p, Centroid: DisplayPointCalculator.CalculateCentroid(p)))
            .OrderBy(part => part.Centroid.Y)
            .ThenBy(part => part.Centroid.X)
            .ToList();

        List<ExportPolygon> created = new(orderedParts.Count);
        for (int i = 0; i < orderedParts.Count; i++)
        {
            string splitId = BuildSplitId(baseId, i + 1);
            created.Add(
                CreateFeature(
                    splitId,
                    orderedParts[i].Polygon,
                    levelId,
                    zoneInfo,
                    name,
                    altName));
        }

        features = created;
        return true;
    }

    public bool TryCreateFloorUnit(
        Floor floor,
        string levelId,
        ICollection<string> warnings,
        out ExportPolygon? feature)
    {
        feature = null;
        if (!TryCreateFloorUnits(floor, levelId, splitMask: null, warnings, out IReadOnlyList<ExportPolygon> features) ||
            features.Count == 0)
        {
            return false;
        }

        feature = features[0];
        return true;
    }

    public bool TryCreateStairsUnit(
        Stairs stairs,
        string levelId,
        ICollection<string> warnings,
        out ExportPolygon? feature)
    {
        feature = null;
        if (stairs is null)
        {
            return false;
        }

        long elementId = stairs.Id.Value;
        if (!TryExtractStairsPolygons(stairs, warnings, out IReadOnlyList<Polygon2D> polygons) || polygons.Count == 0)
        {
            warnings.Add($"Stairs {elementId} geometry could not be extracted.");
            return false;
        }

        feature = CreateFeature(
            sourceElement: stairs,
            polygons: polygons,
            levelId: levelId,
            zoneInfo: _zoneCatalog.StairsDefault,
            warnings: warnings);
        return true;
    }

    public bool TryCreateFamilyUnit(
        FamilyInstance familyInstance,
        string levelId,
        ICollection<string> warnings,
        out ExportPolygon? feature)
    {
        feature = null;
        if (familyInstance is null)
        {
            return false;
        }

        string familyName = GetFamilyName(familyInstance);
        if (!_zoneCatalog.TryGetFamilyInfo(familyName, out ZoneInfo zoneInfo))
        {
            return false;
        }

        long elementId = familyInstance.Id.Value;
        if (!TryExtractElementPolygon(familyInstance, out Polygon2D polygon))
        {
            warnings.Add($"Family instance {elementId} ({familyName}) geometry could not be extracted.");
            return false;
        }

        feature = CreateFeature(
            sourceElement: familyInstance,
            polygon: polygon,
            levelId: levelId,
            zoneInfo: zoneInfo,
            warnings: warnings);
        return true;
    }

    private ExportPolygon CreateFeature(
        Element sourceElement,
        Polygon2D polygon,
        string levelId,
        ZoneInfo zoneInfo,
        ICollection<string> warnings)
    {
        string id = _parameterManager.GetOrCreateElementId(sourceElement, warnings);
        string? name = _parameterManager.GetOptionalStringParameter(sourceElement, SharedParameterManager.ImdfNameParameterName);
        string? altName = _parameterManager.GetOptionalStringParameter(sourceElement, SharedParameterManager.ImdfAltNameParameterName);
        return CreateFeature(id, polygon, levelId, zoneInfo, name, altName);
    }

    private ExportPolygon CreateFeature(
        Element sourceElement,
        IReadOnlyList<Polygon2D> polygons,
        string levelId,
        ZoneInfo zoneInfo,
        ICollection<string> warnings)
    {
        string id = _parameterManager.GetOrCreateElementId(sourceElement, warnings);
        string? name = _parameterManager.GetOptionalStringParameter(sourceElement, SharedParameterManager.ImdfNameParameterName);
        string? altName = _parameterManager.GetOptionalStringParameter(sourceElement, SharedParameterManager.ImdfAltNameParameterName);
        return CreateFeature(id, polygons, levelId, zoneInfo, name, altName);
    }

    private ExportPolygon CreateFeature(
        string id,
        Polygon2D polygon,
        string levelId,
        ZoneInfo zoneInfo,
        string? name,
        string? altName)
    {
        Point2D centroid = DisplayPointCalculator.CalculateCentroid(polygon);
        string displayPoint = DisplayPointCalculator.ToWktPoint(centroid);

        return new ExportPolygon(
            polygon,
            new Dictionary<string, object?>
            {
                ["id"] = id,
                ["category"] = zoneInfo.Category,
                ["restrict"] = zoneInfo.Restriction,
                ["name"] = name,
                ["alt_name"] = altName,
                ["level_id"] = levelId,
                ["source"] = _source,
                ["display_point"] = displayPoint,
            });
    }

    private ExportPolygon CreateFeature(
        string id,
        IReadOnlyList<Polygon2D> polygons,
        string levelId,
        ZoneInfo zoneInfo,
        string? name,
        string? altName)
    {
        if (polygons == null || polygons.Count == 0)
        {
            throw new ArgumentException("At least one polygon is required.", nameof(polygons));
        }

        Polygon2D displayPolygon = polygons
            .OrderByDescending(x => Math.Abs(GetSignedArea(x.ExteriorRing)))
            .First();
        Point2D centroid = DisplayPointCalculator.CalculateCentroid(displayPolygon);
        string displayPoint = DisplayPointCalculator.ToWktPoint(centroid);

        return new ExportPolygon(
            polygons,
            new Dictionary<string, object?>
            {
                ["id"] = id,
                ["category"] = zoneInfo.Category,
                ["restrict"] = zoneInfo.Restriction,
                ["name"] = name,
                ["alt_name"] = altName,
                ["level_id"] = levelId,
                ["source"] = _source,
                ["display_point"] = displayPoint,
            });
    }

    private List<Polygon2D> SplitPolygonByWalls(Polygon2D polygon, Geometry wallLines)
    {
        Polygon? sourcePolygon = ToNtsPolygon(polygon);
        if (sourcePolygon == null || sourcePolygon.IsEmpty)
        {
            return new List<Polygon2D>();
        }

        // Extend wall lines well beyond the polygon bounds so they fully cross it.
        double extendDist = sourcePolygon.EnvelopeInternal.Diameter * 2.0;
        Geometry extendedLines = ExtendLineStrings(wallLines, extendDist);

        Geometry nodedLines;
        try
        {
            // Node the boundary and wall lines together so intersections become shared vertices.
            nodedLines = sourcePolygon.Boundary.Union(extendedLines);
        }
        catch (TopologyException)
        {
            // Fall back to reduced precision when near-coincident coordinates
            // cause a non-noded intersection in the overlay engine.
            var reducer = new GeometryPrecisionReducer(new PrecisionModel(100_000d));
            Geometry reducedBoundary = reducer.Reduce(sourcePolygon.Boundary);
            Geometry reducedLines = reducer.Reduce(extendedLines);
            try
            {
                nodedLines = reducedBoundary.Union(reducedLines);
            }
            catch (TopologyException)
            {
                return new List<Polygon2D> { polygon };
            }
        }

        var polygonizer = new Polygonizer();
        polygonizer.Add(nodedLines);

        var results = new List<Polygon2D>();
        foreach (Geometry geom in polygonizer.GetPolygons())
        {
            if (geom is not Polygon resultPoly || resultPoly.IsEmpty)
            {
                continue;
            }

            // Keep only faces whose interior lies inside the original polygon.
            if (sourcePolygon.Contains(resultPoly.InteriorPoint))
            {
                AddPolygonIfValid(results, resultPoly);
            }
        }

        // If the wall didn't cross the polygon, return the original unsplit.
        return results.Count > 0 ? results : new List<Polygon2D> { polygon };
    }

    private Geometry? TryProjectWallCenterline(Wall wall, ICollection<string> warnings)
    {
        if (wall.Location is not LocationCurve locationCurve)
        {
            return null;
        }

        Curve? curve = locationCurve.Curve;
        if (curve == null)
        {
            return null;
        }

        List<Point2D> pts = ProjectCurve(curve);
        if (pts.Count < 2)
        {
            return null;
        }

        Coordinate[] coords = pts.Select(p => new Coordinate(p.X, p.Y)).ToArray();
        LineString line = GeometryFactory.CreateLineString(coords);
        return line.IsEmpty ? null : line;
    }

    private bool TryExtractStairsPolygons(
        Stairs stairs,
        ICollection<string> warnings,
        out IReadOnlyList<Polygon2D> polygons)
    {
        polygons = Array.Empty<Polygon2D>();
        List<Polygon2D> footprintPolygons = new();

        foreach (ElementId runId in stairs.GetStairsRuns())
        {
            if (_document.GetElement(runId) is not StairsRun run)
            {
                continue;
            }

            try
            {
                CurveLoop runBoundary = run.GetFootprintBoundary();
                if (TryCreatePolygonFromCurveLoop(runBoundary, out Polygon2D runPolygon))
                {
                    footprintPolygons.Add(runPolygon);
                }
            }
            catch (Exception)
            {
                warnings.Add($"Stairs run {run.Id.Value} footprint boundary could not be read.");
            }
        }

        foreach (ElementId landingId in stairs.GetStairsLandings())
        {
            if (_document.GetElement(landingId) is not StairsLanding landing)
            {
                continue;
            }

            try
            {
                CurveLoop landingBoundary = landing.GetFootprintBoundary();
                if (TryCreatePolygonFromCurveLoop(landingBoundary, out Polygon2D landingPolygon))
                {
                    footprintPolygons.Add(landingPolygon);
                }
            }
            catch (Exception)
            {
                warnings.Add($"Stairs landing {landing.Id.Value} footprint boundary could not be read.");
            }
        }

        if (footprintPolygons.Count == 0)
        {
            if (!TryExtractElementPolygon(stairs, out Polygon2D fallback))
            {
                return false;
            }

            polygons = new[] { fallback };
            return true;
        }

        List<Geometry> geometries = new();
        foreach (Polygon2D poly in footprintPolygons)
        {
            Polygon? ntsPolygon = ToNtsPolygon(poly);
            if (ntsPolygon != null && !ntsPolygon.IsEmpty)
            {
                geometries.Add(ntsPolygon);
            }
        }

        if (geometries.Count == 0)
        {
            return false;
        }

        Geometry unioned = UnaryUnionOp.Union(geometries);
        if (unioned.IsEmpty)
        {
            return false;
        }

        Geometry normalized = unioned.Buffer(0d);
        List<Polygon2D> extracted = ExtractPolygons(normalized);
        if (extracted.Count == 0)
        {
            return false;
        }

        polygons = extracted;
        return true;
    }

    private bool TryCreatePolygonFromCurveLoop(CurveLoop loop, out Polygon2D polygon)
    {
        polygon = null!;
        if (loop == null)
        {
            return false;
        }

        List<Point2D> points = ProjectCurveLoop(loop, closeLoop: true);
        if (points.Count < 4)
        {
            return false;
        }

        polygon = new Polygon2D(points);
        return true;
    }

    private bool TryExtractElementPolygon(Element element, out Polygon2D polygon)
    {
        polygon = null!;

        if (element is Floor floor)
        {
            // 1. Bottom faces — most reliable for slabs modelled correctly.
            List<List<XYZ>> loops = ExtractLoopsFromFloorBottomFaces(floor);

            // 2. Top faces — same projected footprint; helps thin/reversed floors.
            if (loops.Count == 0)
            {
                loops = ExtractLoopsFromFloorTopFaces(floor);
            }

            // 3. Solid geometry (visible objects only).
            if (loops.Count == 0)
            {
                loops = ExtractLoopsFromSolidGeometry(element, includeNonVisibleObjects: false);
            }

            // 4. Solid geometry (include non-visible objects).
            if (loops.Count == 0)
            {
                loops = ExtractLoopsFromSolidGeometry(element, includeNonVisibleObjects: true);
            }

            // 5. Sketch — the definitive user-drawn boundary; most reliable fallback.
            if (loops.Count == 0)
            {
                return TryExtractFloorPolygonFromSketch(floor, out polygon);
            }

            return BuildPolygonFromLoops(loops, out polygon);
        }
        else
        {
            List<List<XYZ>> loops = ExtractLoopsFromSolidGeometry(element, includeNonVisibleObjects: false);
            if (loops.Count == 0)
            {
                return false;
            }

            return BuildPolygonFromLoops(loops, out polygon);
        }
    }

    private bool BuildPolygonFromLoops(List<List<XYZ>> loops, out Polygon2D polygon)
    {
        polygon = null!;

        List<List<Point2D>> projectedLoops = new();
        foreach (List<XYZ> loop in loops)
        {
            List<Point2D> ring = ProjectLoop(loop);
            if (ring.Count >= 4)
            {
                projectedLoops.Add(ring);
            }
        }

        if (projectedLoops.Count == 0)
        {
            return false;
        }

        int exteriorIndex = GetExteriorLoopIndex(projectedLoops);
        IReadOnlyList<Point2D> exterior = projectedLoops[exteriorIndex];
        List<IReadOnlyList<Point2D>> holes = new();
        for (int i = 0; i < projectedLoops.Count; i++)
        {
            if (i != exteriorIndex)
            {
                holes.Add(projectedLoops[i]);
            }
        }

        polygon = new Polygon2D(exterior, holes);
        return true;
    }

    private bool TryExtractFloorPolygonFromSketch(Floor floor, out Polygon2D polygon)
    {
        polygon = null!;
        if (floor.SketchId == ElementId.InvalidElementId)
        {
            return false;
        }

        if (_document.GetElement(floor.SketchId) is not Sketch sketch)
        {
            return false;
        }

        List<List<Point2D>> projectedLoops = new();
        foreach (CurveArray curveArray in sketch.Profile)
        {
            CurveLoop loop = CurveLoop.Create(curveArray.Cast<Curve>().ToList());
            List<Point2D> points = ProjectCurveLoop(loop, closeLoop: true);
            if (points.Count >= 4)
            {
                projectedLoops.Add(points);
            }
        }

        if (projectedLoops.Count == 0)
        {
            return false;
        }

        int exteriorIndex = GetExteriorLoopIndex(projectedLoops);
        List<IReadOnlyList<Point2D>> holes = projectedLoops
            .Where((_, i) => i != exteriorIndex)
            .Cast<IReadOnlyList<Point2D>>()
            .ToList();
        polygon = new Polygon2D(projectedLoops[exteriorIndex], holes);
        return true;
    }

    private static List<List<XYZ>> ExtractLoopsFromFloorBottomFaces(Floor floor)
    {
        List<List<XYZ>> loops = new();
        IList<Reference>? references = HostObjectUtils.GetBottomFaces(floor);
        if (references == null)
        {
            return loops;
        }

        foreach (Reference reference in references)
        {
            if (floor.GetGeometryObjectFromReference(reference) is Face face)
            {
                loops.AddRange(ExtractLoopsFromFace(face));
            }
        }

        return loops;
    }

    private static List<List<XYZ>> ExtractLoopsFromFloorTopFaces(Floor floor)
    {
        List<List<XYZ>> loops = new();
        IList<Reference>? references = HostObjectUtils.GetTopFaces(floor);
        if (references == null)
        {
            return loops;
        }

        foreach (Reference reference in references)
        {
            if (floor.GetGeometryObjectFromReference(reference) is Face face)
            {
                loops.AddRange(ExtractLoopsFromFace(face));
            }
        }

        return loops;
    }

    private static List<List<XYZ>> ExtractLoopsFromSolidGeometry(Element element, bool includeNonVisibleObjects)
    {
        Options options = new()
        {
            DetailLevel = ViewDetailLevel.Fine,
            ComputeReferences = false,
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
        foreach (Solid solid in solids)
        {
            if (solid.Volume <= 0)
            {
                continue;
            }

            foreach (Face face in solid.Faces)
            {
                if (face is not PlanarFace planarFace)
                {
                    continue;
                }

                XYZ normal = planarFace.FaceNormal;
                if (normal.Z >= -0.9d)
                {
                    continue;
                }

                if (planarFace.Origin.Z < lowestZ)
                {
                    lowestZ = planarFace.Origin.Z;
                    lowestFace = planarFace;
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
                case Solid solid when solid.Volume > 0:
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
                foreach (XYZ point in tessellated)
                {
                    if (loop.Count == 0 || !IsSamePoint(loop[loop.Count - 1], point))
                    {
                        loop.Add(point);
                    }
                }
            }

            if (loop.Count < 3)
            {
                continue;
            }

            if (!IsSamePoint(loop[0], loop[loop.Count - 1]))
            {
                loop.Add(loop[0]);
            }

            loops.Add(loop);
        }

        return loops;
    }

    private List<Point2D> ProjectLoop(IReadOnlyList<XYZ> loop)
    {
        List<Point2D> result = new(loop.Count);
        foreach (XYZ point in loop)
        {
            Point2D projected = ProjectPoint(point);
            if (result.Count == 0 || !IsSamePoint(result[result.Count - 1], projected))
            {
                result.Add(projected);
            }
        }

        if (result.Count >= 3 && !IsSamePoint(result[0], result[result.Count - 1]))
        {
            result.Add(result[0]);
        }

        return result;
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
        IList<XYZ> sampled = curve.Tessellate();
        if (sampled.Count == 0)
        {
            sampled = new List<XYZ>
            {
                curve.GetEndPoint(0),
                curve.GetEndPoint(1),
            };
        }

        List<Point2D> points = new(sampled.Count);
        for (int i = 0; i < sampled.Count; i++)
        {
            Point2D projected = ProjectPoint(sampled[i]);
            if (points.Count == 0 || !IsSamePoint(points[points.Count - 1], projected))
            {
                points.Add(projected);
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

    private static string BuildSplitId(string baseId, int splitOrdinal)
    {
        string seed = string.Concat(baseId, ":", splitOrdinal.ToString(CultureInfo.InvariantCulture));
        using MD5 md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
        byte[] guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);

        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x30);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes).ToString();
    }

    private static Geometry ExtendLineStrings(Geometry geometry, double distance)
    {
        List<LineString> extended = new();
        CollectAndExtendLineStrings(geometry, distance, extended);
        if (extended.Count == 0)
        {
            return geometry;
        }

        return GeometryFactory.CreateMultiLineString(extended.ToArray());
    }

    private static void CollectAndExtendLineStrings(Geometry geometry, double distance, List<LineString> result)
    {
        if (geometry is LineString ls)
        {
            result.Add(ExtendLineString(ls, distance));
        }
        else
        {
            for (int i = 0; i < geometry.NumGeometries; i++)
            {
                CollectAndExtendLineStrings(geometry.GetGeometryN(i), distance, result);
            }
        }
    }

    private static LineString ExtendLineString(LineString line, double distance)
    {
        if (line.NumPoints < 2)
        {
            return line;
        }

        Coordinate[] coords = line.Coordinates;

        // Extend start point backwards along the first segment.
        Coordinate start = coords[0];
        Coordinate afterStart = coords[1];
        double startDx = start.X - afterStart.X;
        double startDy = start.Y - afterStart.Y;
        double startLen = Math.Sqrt(startDx * startDx + startDy * startDy);
        if (startLen > 0)
        {
            start = new Coordinate(
                start.X + (startDx / startLen) * distance,
                start.Y + (startDy / startLen) * distance);
        }

        // Extend end point forwards along the last segment.
        Coordinate end = coords[coords.Length - 1];
        Coordinate beforeEnd = coords[coords.Length - 2];
        double endDx = end.X - beforeEnd.X;
        double endDy = end.Y - beforeEnd.Y;
        double endLen = Math.Sqrt(endDx * endDx + endDy * endDy);
        if (endLen > 0)
        {
            end = new Coordinate(
                end.X + (endDx / endLen) * distance,
                end.Y + (endDy / endLen) * distance);
        }

        Coordinate[] newCoords = new Coordinate[coords.Length];
        newCoords[0] = start;
        for (int i = 1; i < coords.Length - 1; i++)
        {
            newCoords[i] = coords[i];
        }

        newCoords[coords.Length - 1] = end;
        return GeometryFactory.CreateLineString(newCoords);
    }

    private static Polygon? ToNtsPolygon(Polygon2D polygon)
    {
        if (!TryCreateLinearRing(polygon.ExteriorRing, out LinearRing? shell))
        {
            return null;
        }

        List<LinearRing> holes = new();
        for (int i = 0; i < polygon.InteriorRings.Count; i++)
        {
            if (TryCreateLinearRing(polygon.InteriorRings[i], out LinearRing? hole) && hole != null)
            {
                holes.Add(hole);
            }
        }

        Polygon created = GeometryFactory.CreatePolygon(shell, holes.ToArray());
        if (!created.IsValid)
        {
            Geometry healed = created.Buffer(0d);
            if (healed is Polygon healedPolygon)
            {
                return healedPolygon;
            }

            if (healed is MultiPolygon healedMulti && healedMulti.NumGeometries > 0)
            {
                return healedMulti.GetGeometryN(0) as Polygon;
            }
        }

        return created;
    }

    private static bool TryCreateLinearRing(IReadOnlyList<Point2D> ringPoints, out LinearRing? ring)
    {
        ring = null;
        if (ringPoints == null || ringPoints.Count < 4)
        {
            return false;
        }

        List<Coordinate> coords = new(ringPoints.Count + 1);
        for (int i = 0; i < ringPoints.Count; i++)
        {
            coords.Add(new Coordinate(ringPoints[i].X, ringPoints[i].Y));
        }

        Coordinate first = coords[0];
        Coordinate last = coords[coords.Count - 1];
        if (!first.Equals2D(last))
        {
            coords.Add(new Coordinate(first.X, first.Y));
        }

        if (coords.Count < 4)
        {
            return false;
        }

        ring = GeometryFactory.CreateLinearRing(coords.ToArray());
        return !ring.IsEmpty;
    }

    private static List<Polygon2D> ExtractPolygons(Geometry geometry)
    {
        List<Polygon2D> polygons = new();
        switch (geometry)
        {
            case Polygon polygon:
                AddPolygonIfValid(polygons, polygon);
                break;
            case MultiPolygon multiPolygon:
                for (int i = 0; i < multiPolygon.NumGeometries; i++)
                {
                    if (multiPolygon.GetGeometryN(i) is Polygon child)
                    {
                        AddPolygonIfValid(polygons, child);
                    }
                }

                break;
            case GeometryCollection collection:
                for (int i = 0; i < collection.NumGeometries; i++)
                {
                    polygons.AddRange(ExtractPolygons(collection.GetGeometryN(i)));
                }

                break;
        }

        return polygons;
    }

    private static void AddPolygonIfValid(ICollection<Polygon2D> target, Polygon polygon)
    {
        if (polygon.IsEmpty || polygon.Area < MinSplitAreaSquareMeters)
        {
            return;
        }

        Polygon2D? converted = ToPolygon2D(polygon);
        if (converted != null)
        {
            target.Add(converted);
        }
    }

    private static Polygon2D? ToPolygon2D(Polygon polygon)
    {
        IReadOnlyList<Point2D>? exterior = ToPointList(polygon.ExteriorRing.Coordinates);
        if (exterior == null)
        {
            return null;
        }

        List<IReadOnlyList<Point2D>> interior = new();
        for (int i = 0; i < polygon.NumInteriorRings; i++)
        {
            IReadOnlyList<Point2D>? ring = ToPointList(polygon.GetInteriorRingN(i).Coordinates);
            if (ring != null)
            {
                interior.Add(ring);
            }
        }

        return new Polygon2D(exterior, interior);
    }

    private static IReadOnlyList<Point2D>? ToPointList(Coordinate[] coordinates)
    {
        if (coordinates == null || coordinates.Length < 4)
        {
            return null;
        }

        List<Point2D> points = new(coordinates.Length);
        for (int i = 0; i < coordinates.Length; i++)
        {
            points.Add(new Point2D(coordinates[i].X, coordinates[i].Y));
        }

        return points;
    }

    private static int GetExteriorLoopIndex(IReadOnlyList<IReadOnlyList<Point2D>> loops)
    {
        int index = 0;
        double maxArea = double.MinValue;
        for (int i = 0; i < loops.Count; i++)
        {
            double area = Math.Abs(GetSignedArea(loops[i]));
            if (area > maxArea)
            {
                maxArea = area;
                index = i;
            }
        }

        return index;
    }

    private static bool IsSamePoint(XYZ left, XYZ right)
    {
        return left.DistanceTo(right) <= 1e-8d;
    }

    private static bool IsSamePoint(Point2D left, Point2D right)
    {
        return Math.Abs(left.X - right.X) <= 1e-8d &&
               Math.Abs(left.Y - right.Y) <= 1e-8d;
    }

    private static double GetSignedArea(IReadOnlyList<Point2D> ring)
    {
        double sum = 0d;
        for (int i = 0; i < ring.Count - 1; i++)
        {
            Point2D current = ring[i];
            Point2D next = ring[i + 1];
            sum += (current.X * next.Y) - (next.X * current.Y);
        }

        return sum * 0.5d;
    }

    private string GetElementTypeName(Element element)
    {
        Element? typeElement = _document.GetElement(element.GetTypeId());
        string? name = (typeElement as ElementType)?.Name;
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name!.Trim();
        }

        return element.Name;
    }

    public static string GetFamilyName(FamilyInstance familyInstance)
    {
        string? familyName = familyInstance.Symbol?.FamilyName;
        if (string.IsNullOrWhiteSpace(familyName))
        {
            familyName = familyInstance.Symbol?.Family?.Name;
        }

        return string.IsNullOrWhiteSpace(familyName) ? "<unknown-family>" : familyName!.Trim();
    }
}
