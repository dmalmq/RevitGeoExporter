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
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Core.Schema;
using RevitGeoExporter.Export;

namespace RevitGeoExporter.Extractors;

public sealed class UnitExtractor
{
    private const double MinSplitAreaSquareMeters = 0.05d;
    private const double SquareFeetToSquareMeters = 0.09290304d;
    private const double FloorAreaFallbackRatio = 0.85d;
    private const double MinFloorAreaForSanityCheckSquareMeters = 0.25d;
    private const double ProjectedTriangleMinAreaSquareMeters = 1e-8d;
    private static readonly string[] FloorNamePrefixes = { "j ", "j　", "j" };
    private static readonly string[] FloorNameSuffixes =
    {
        ZoneNameParser.DefaultSuffix,
        "_床",
        "＿床",
    };

    private static readonly GeometryFactory GeometryFactory =
        new(new PrecisionModel(1_000_000d));

    private readonly Document _document;
    private readonly SharedCoordinateProjector _sharedCoordinateProjector;
    private readonly ExportSourceDescriptor _sourceDescriptor;
    private readonly ZoneCatalog _zoneCatalog;
    private readonly IExportMetadataProvider _metadataProvider;
    private readonly FloorCategoryResolver _floorCategoryResolver;
    private readonly RoomCategoryResolver _roomCategoryResolver;
    private readonly IReadOnlyDictionary<string, string> _familyCategoryOverrides;
    private readonly string _source;
    private readonly SchemaProfile _schemaProfile;
    private readonly PreviewPaletteResolver _paletteResolver = new();
    private readonly StairVisibilityResolver _stairVisibilityResolver;
    private readonly bool _simplifyEscalatorUnits;

    internal View3D? CurrentGeometryView { get; private set; }

    internal void SetCurrentGeometryView(View3D? geometryView)
    {
        CurrentGeometryView = geometryView;
        _stairVisibilityResolver.CurrentGeometryView = geometryView;
    }

    public UnitExtractor(
        Document document,
        ZoneCatalog zoneCatalog,
        IExportMetadataProvider metadataProvider,
        string source,
        FloorCategoryResolver floorCategoryResolver,
        RoomCategoryResolver roomCategoryResolver,
        IReadOnlyDictionary<string, string>? familyCategoryOverrides = null,
        ExportSourceDescriptor? sourceDescriptor = null,
        SchemaProfile? schemaProfile = null,
        bool simplifyEscalatorUnits = false)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _sourceDescriptor = sourceDescriptor ?? ExportSourceDescriptor.CreateHost(_document);
        _sharedCoordinateProjector = new SharedCoordinateProjector(_sourceDescriptor.ProjectionProjectLocation);
        _zoneCatalog = zoneCatalog ?? throw new ArgumentNullException(nameof(zoneCatalog));
        _metadataProvider = metadataProvider ?? throw new ArgumentNullException(nameof(metadataProvider));
        _floorCategoryResolver =
            floorCategoryResolver ?? throw new ArgumentNullException(nameof(floorCategoryResolver));
        _roomCategoryResolver =
            roomCategoryResolver ?? throw new ArgumentNullException(nameof(roomCategoryResolver));
        _familyCategoryOverrides = familyCategoryOverrides ??
            new Dictionary<string, string>(StringComparer.Ordinal);
        _source = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim();
        _schemaProfile = schemaProfile?.Clone() ?? SchemaProfile.CreateCoreProfile();
        _simplifyEscalatorUnits = simplifyEscalatorUnits;
        _stairVisibilityResolver = new StairVisibilityResolver(
            _document,
            ProjectPoint,
            point => _sourceDescriptor.TransformToHost.OfPoint(point));
    }

    public bool TryCreateFloorUnits(
        Floor floor,
        string levelId,
        string? viewName,
        ICollection<string> warnings,
        out IReadOnlyList<ExportPolygon> features)
    {
        features = Array.Empty<ExportPolygon>();
        if (floor is null)
        {
            return false;
        }

        long elementId = floor.Id.Value;
        string typeName = GetElementTypeName(floor);
        string rawFloorTypeName = string.IsNullOrWhiteSpace(typeName) ? $"<floor-{elementId}>" : typeName.Trim();
        string zoneName;
        if (TryResolveFloorZoneName(typeName, out string parsedZoneName, out bool prefixMatched))
        {
            zoneName = parsedZoneName;
            if (!prefixMatched)
            {
                warnings.Add(
                    $"Floor {elementId} type '{typeName}' is missing the expected '{ZoneNameParser.DefaultPrefix}' prefix. Parsed zone '{zoneName}' using suffix matching.");
            }
        }
        else
        {
            zoneName = rawFloorTypeName;
            warnings.Add(
                $"Floor {elementId} type '{typeName}' does not match the expected floor naming convention. Using full type name '{zoneName}' for zone lookup.");
        }

        if (!TryExtractElementPolygons(floor, out List<Polygon2D> basePolygons))
        {
            warnings.Add($"Floor {elementId} geometry could not be extracted.");
            return false;
        }

        if (CurrentGeometryView == null &&
            TryGetFloorAreaSquareMeters(floor, out double expectedFloorAreaSquareMeters) &&
            expectedFloorAreaSquareMeters >= MinFloorAreaForSanityCheckSquareMeters)
        {
            double extractedAreaSquareMeters = ComputeTotalAreaSquareMeters(basePolygons);
            if (extractedAreaSquareMeters < (expectedFloorAreaSquareMeters * FloorAreaFallbackRatio) &&
                TryExtractFloorPolygonsFromSketch(floor, out List<Polygon2D> sketchPolygons))
            {
                double sketchAreaSquareMeters = ComputeTotalAreaSquareMeters(sketchPolygons);
                if (sketchAreaSquareMeters > extractedAreaSquareMeters)
                {
                    warnings.Add(
                        $"Floor {elementId} geometry extraction area ({extractedAreaSquareMeters:F2} m²) was significantly below Revit floor area ({expectedFloorAreaSquareMeters:F2} m²). Using sketch profile fallback ({sketchAreaSquareMeters:F2} m²).");
                    basePolygons = sketchPolygons;
                }
            }
        }

        basePolygons = ClipPolygonsToSectionBox(basePolygons, $"Floor {elementId}", warnings);
        if (basePolygons.Count == 0)
        {
            return false;
        }

        ResolvedFloorCategory resolvedFloorCategory = _floorCategoryResolver.Resolve(rawFloorTypeName, zoneName);
        ResolvedMappingCategory resolvedCategory = new(
            "floor",
            resolvedFloorCategory.FloorTypeName,
            resolvedFloorCategory.ParsedZoneCandidate,
            null,
            resolvedFloorCategory.ZoneInfo,
            resolvedFloorCategory.ResolutionSource,
            resolvedFloorCategory.IsUnassigned);
        ZoneInfo zoneInfo = resolvedCategory.ZoneInfo;
        if (resolvedCategory.IsUnassigned)
        {
            warnings.Add(
                $"Floor {elementId} zone '{zoneName}' was not found in catalog. Default category/restriction applied.");
        }

        ExportElementMetadata floorMetadata = _metadataProvider.GetElementMetadata(floor, warnings);
        string baseId = floorMetadata.ExportId;
        string? name = _metadataProvider.GetOptionalStringParameter(floor, SharedParameterManager.ImdfNameParameterName);
        string? altName = _metadataProvider.GetOptionalStringParameter(floor, SharedParameterManager.ImdfAltNameParameterName);

        // Create features for each base polygon extracted from the floor.
        List<Polygon2D> allPolygons = basePolygons;

        if (allPolygons.Count == 1)
        {
            features = new[]
            {
                CreateFeature(
                    baseId,
                    allPolygons[0],
                    levelId,
                    zoneInfo,
                    name,
                    altName,
                    floor,
                    floor.Id.Value,
                    floorMetadata,
                    resolvedCategory,
                    rawFloorTypeName,
                    viewName),
            };
            return true;
        }

        List<(Polygon2D Polygon, Point2D Centroid)> orderedParts = allPolygons
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
                    altName,
                    floor,
                    floor.Id.Value,
                    floorMetadata,
                    resolvedCategory,
                    rawFloorTypeName,
                    viewName));
        }

        features = created;
        return true;
    }

    public bool TryCreateFloorUnit(
        Floor floor,
        string levelId,
        string? viewName,
        ICollection<string> warnings,
        out ExportPolygon? feature)
    {
        feature = null;
        if (!TryCreateFloorUnits(floor, levelId, viewName, warnings, out IReadOnlyList<ExportPolygon> features) ||
            features.Count == 0)
        {
            return false;
        }

        feature = features[0];
        return true;
    }


    public bool TryCreateRoomUnit(
        Room room,
        string levelId,
        string roomCategoryParameterName,
        string? viewName,
        ICollection<string> warnings,
        out ExportPolygon? feature)
    {
        feature = null;
        if (room is null)
        {
            return false;
        }

        string mappingValue = GetRoomCategoryValue(room, roomCategoryParameterName);
        ResolvedMappingCategory resolvedCategory = _roomCategoryResolver.Resolve(mappingValue, roomCategoryParameterName);
        if (resolvedCategory.IsUnassigned)
        {
            warnings.Add(
                $"Room {room.Id.Value} value '{mappingValue}' for parameter '{roomCategoryParameterName}' was not mapped to a known category. Default category/restriction applied.");
        }

        if (!TryExtractRoomPolygons(room, out List<Polygon2D> polygons) || polygons.Count == 0)
        {
            warnings.Add($"Room {room.Id.Value} geometry could not be extracted.");
            return false;
        }

        polygons = ClipPolygonsToSectionBox(polygons, $"Room {room.Id.Value}", warnings);
        if (polygons.Count == 0)
        {
            return false;
        }

        ExportElementMetadata roomMetadata = _metadataProvider.GetElementMetadata(room, warnings);
        string? imdfName = _metadataProvider.GetOptionalStringParameter(room, SharedParameterManager.ImdfNameParameterName);
        string? imdfAltName = _metadataProvider.GetOptionalStringParameter(room, SharedParameterManager.ImdfAltNameParameterName);
        string? name = string.IsNullOrWhiteSpace(imdfName) ? room.Name : imdfName;
        string? altName = string.IsNullOrWhiteSpace(imdfAltName) ? room.Number : imdfAltName;

        feature = CreateFeature(
            roomMetadata.ExportId,
            polygons,
            levelId,
            resolvedCategory.ZoneInfo,
            name,
            altName,
            room,
            room.Id.Value,
            roomMetadata,
            resolvedCategory,
            sourceLabel: roomCategoryParameterName,
            viewName: viewName);
        return true;
    }

    private const double SimplifiedStairDepthMeters = 1.75d;

    internal List<Polygon2D>? TrySimplifyStairPolygons(
        Stairs stair,
        Level viewLevel,
        IReadOnlyList<Polygon2D> visiblePolygons,
        ICollection<string> warnings)
    {
        if (visiblePolygons == null || visiblePolygons.Count == 0)
        {
            return null;
        }

        ElementId? baseLevelId = GetStairsBaseLevelId(stair);
        ElementId? topLevelId = GetStairsTopLevelId(stair);

        bool needBeginning = baseLevelId != null && baseLevelId.Value == viewLevel.Id.Value;
        bool needEnd = topLevelId != null && topLevelId.Value == viewLevel.Id.Value;

        if (!needBeginning && !needEnd)
        {
            return null;
        }

        List<StairsRun> runs = GetStairsRunsByElevation(stair);
        if (runs.Count == 0)
        {
            return visiblePolygons.ToList();
        }

        Geometry? fullGeometry = UnionPolygonsToNtsGeometry(visiblePolygons);
        if (fullGeometry == null || fullGeometry.IsEmpty)
        {
            return visiblePolygons.ToList();
        }

        List<Geometry> clippedParts = new();

        if (needBeginning)
        {
            StairsRun firstRun = runs[0];
            Geometry? beginClip = TryCreateStairEntryClip(firstRun, fromStart: true);
            if (beginClip != null)
            {
                Geometry? intersected = IntersectSafe(fullGeometry, beginClip);
                if (intersected != null && !intersected.IsEmpty)
                {
                    clippedParts.Add(intersected);
                }
            }
        }

        if (needEnd)
        {
            StairsRun lastRun = runs[runs.Count - 1];
            Geometry? endClip = TryCreateStairEntryClip(lastRun, fromStart: false);
            if (endClip != null)
            {
                Geometry? intersected = IntersectSafe(fullGeometry, endClip);
                if (intersected != null && !intersected.IsEmpty)
                {
                    clippedParts.Add(intersected);
                }
            }
        }

        if (clippedParts.Count == 0)
        {
            return null;
        }

        Geometry simplified;
        if (clippedParts.Count == 1)
        {
            simplified = clippedParts[0];
        }
        else
        {
            try
            {
                simplified = UnaryUnionOp.Union(clippedParts).Buffer(0d);
            }
            catch (TopologyException)
            {
                return null;
            }
        }

        return ExtractPolygons(simplified);
    }

    private static ElementId? GetStairsBaseLevelId(Stairs stairs)
    {
        Parameter? param = stairs.get_Parameter(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM);
        return param?.AsElementId();
    }

    private static ElementId? GetStairsTopLevelId(Stairs stairs)
    {
        Parameter? param = stairs.get_Parameter(BuiltInParameter.STAIRS_TOP_LEVEL_PARAM);
        return param?.AsElementId();
    }

    private List<StairsRun> GetStairsRunsByElevation(Stairs stairs)
    {
        List<StairsRun> runs = new();
        foreach (ElementId runId in stairs.GetStairsRuns())
        {
            if (_document.GetElement(runId) is StairsRun run)
            {
                runs.Add(run);
            }
        }

        runs.Sort((a, b) => a.BaseElevation.CompareTo(b.BaseElevation));
        return runs;
    }

    private Geometry? TryCreateStairEntryClip(StairsRun run, bool fromStart)
    {
        try
        {
            CurveLoop pathLoop = run.GetStairsPath();
            List<Point2D> pathPoints = ProjectCurveLoop(pathLoop, closeLoop: false);
            if (pathPoints.Count < 2)
            {
                return null;
            }

            double pathLength = GetPolylineLength(pathPoints);
            if (pathLength < SimplifiedStairDepthMeters * 0.2d)
            {
                return null;
            }

            double fraction = fromStart
                ? Math.Min(1d, SimplifiedStairDepthMeters / pathLength)
                : Math.Max(0d, 1d - (SimplifiedStairDepthMeters / pathLength));

            if (!TryInterpolateOnPolyline(pathPoints, fraction, out Point2D cutPoint, out Point2D tangent))
            {
                return null;
            }

            Point2D perp = new(-tangent.Y, tangent.X);
            double span = Math.Max(20d, pathLength * 4d);

            double px = cutPoint.X;
            double py = cutPoint.Y;
            double nx = perp.X;
            double ny = perp.Y;
            double tx = tangent.X;
            double ty = tangent.Y;

            Point2D a = new(px + (nx * span), py + (ny * span));
            Point2D b = new(px - (nx * span), py - (ny * span));

            Point2D c;
            Point2D d;
            if (fromStart)
            {
                c = new(b.X - (tx * span), b.Y - (ty * span));
                d = new(a.X - (tx * span), a.Y - (ty * span));
            }
            else
            {
                c = new(b.X + (tx * span), b.Y + (ty * span));
                d = new(a.X + (tx * span), a.Y + (ty * span));
            }

            Coordinate[] coords = new[]
            {
                new Coordinate(a.X, a.Y),
                new Coordinate(b.X, b.Y),
                new Coordinate(c.X, c.Y),
                new Coordinate(d.X, d.Y),
                new Coordinate(a.X, a.Y),
            };
            LinearRing ring = GeometryFactory.CreateLinearRing(coords);
            return GeometryFactory.CreatePolygon(ring);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Geometry? UnionPolygonsToNtsGeometry(IReadOnlyList<Polygon2D> polygons)
    {
        List<Geometry> geoms = new();
        foreach (Polygon2D polygon in polygons)
        {
            Geometry? geom = ToNtsGeometry(polygon);
            if (geom != null && !geom.IsEmpty)
            {
                geoms.Add(geom);
            }
        }

        if (geoms.Count == 0)
        {
            return null;
        }

        return geoms.Count == 1 ? geoms[0] : UnaryUnionOp.Union(geoms).Buffer(0d);
    }

    private static Geometry? IntersectSafe(Geometry source, Geometry clip)
    {
        try
        {
            return source.Intersection(clip).Buffer(0d);
        }
        catch (TopologyException)
        {
            return null;
        }
    }

    public bool TryCreateStairsUnit(
        Stairs stairs,
        ViewPlan? view,
        string levelId,
        ICollection<string> warnings,
        out ExportPolygon? feature)
    {
        feature = null;
        if (stairs is null)
        {
            return false;
        }

        if (!TryResolveStairVisibility(stairs, view, warnings, out VerticalCirculationVisibilityResult? visibility) ||
            !TryCreateStairsUnit(stairs, visibility.VisiblePolygons, levelId, view?.Name, warnings, out feature))
        {
            return false;
        }

        return true;
    }

    internal bool TryCreateStairsUnit(
        Stairs stairs,
        IReadOnlyList<Polygon2D> polygons,
        string levelId,
        string? viewName,
        ICollection<string> warnings,
        out ExportPolygon? feature)
    {
        feature = null;
        if (stairs is null)
        {
            return false;
        }

        long elementId = stairs.Id.Value;
        if (polygons == null || polygons.Count == 0)
        {
            warnings.Add($"Stairs {elementId} geometry could not be extracted.");
            return false;
        }

        IReadOnlyList<Polygon2D> clippedPolygons = ClipPolygonsToSectionBox(polygons, $"Stairs {elementId}", warnings);
        if (clippedPolygons.Count == 0)
        {
            return false;
        }

        feature = CreateFeature(
            sourceElement: stairs,
            polygons: clippedPolygons,
            levelId: levelId,
            zoneInfo: _zoneCatalog.StairsDefault,
            sourceLabel: "stairs",
            viewName: viewName,
            warnings: warnings);
        return true;
    }

    internal bool TryResolveStairVisibility(
        Stairs stairs,
        ViewPlan? view,
        ICollection<string> warnings,
        out VerticalCirculationVisibilityResult result)
    {
        return _stairVisibilityResolver.TryResolveVisibleStair(stairs, view, warnings, out result);
    }

    internal VerticalCirculationVisibilityResult ApplyStairOcclusionMask(
        Stairs stairs,
        VerticalCirculationVisibilityResult result,
        Geometry? stairOcclusionMask,
        ICollection<string> warnings)
    {
        return _stairVisibilityResolver.ApplyOcclusionMask(stairs, result, stairOcclusionMask, warnings);
    }

    public bool TryCreateFamilyUnit(
        FamilyInstance familyInstance,
        ViewPlan? view,
        string levelId,
        ICollection<string> warnings,
        out ExportPolygon? feature)
    {
        return TryCreateFamilyUnit(familyInstance, view, levelId, warnings, out feature, out _);
    }

    public bool TryCreateFamilyUnit(
        FamilyInstance familyInstance,
        ViewPlan? view,
        string levelId,
        ICollection<string> warnings,
        out ExportPolygon? feature,
        out string? resolvedCategory)
    {
        feature = null;
        resolvedCategory = null;
        if (familyInstance is null)
        {
            return false;
        }

        if (!TryResolveFamilyUnitZoneInfo(familyInstance, out string familyName, out ZoneInfo zoneInfo))
        {
            return false;
        }

        resolvedCategory = zoneInfo.Category;

        if (string.Equals(zoneInfo.Category, "escalator", StringComparison.OrdinalIgnoreCase))
        {
            if (TryResolveEscalatorVisibility(
                    familyInstance,
                    view,
                    warnings,
                    out VerticalCirculationVisibilityResult visibility) &&
                TryCreateEscalatorUnit(
                    familyInstance,
                    visibility.VisiblePolygons,
                    levelId,
                    view?.Name,
                    warnings,
                    out feature,
                    view))
            {
                return true;
            }

            return false;
        }

        long elementId = familyInstance.Id.Value;
        if (!TryExtractFamilyUnitPolygons(familyInstance, view, out List<Polygon2D> polygons))
        {
            warnings.Add($"Family instance {elementId} ({familyName}) geometry could not be extracted.");
            return false;
        }

        polygons = ClipPolygonsToSectionBox(polygons, $"Family instance {elementId}", warnings);
        if (polygons.Count == 0)
        {
            return false;
        }

        feature = CreateFeature(
            sourceElement: familyInstance,
            polygons: polygons,
            levelId: levelId,
            zoneInfo: zoneInfo,
            sourceLabel: familyName,
            viewName: view?.Name,
            warnings: warnings);
        return true;
    }

    internal bool TryResolveFamilyUnitZoneInfo(
        FamilyInstance familyInstance,
        out string familyName,
        out ZoneInfo zoneInfo)
    {
        familyName = GetFamilyName(familyInstance);
        return TryResolveFamilyZoneInfo(familyName, out zoneInfo);
    }

    internal bool TryCreateEscalatorUnit(
        FamilyInstance escalator,
        IReadOnlyList<Polygon2D> polygons,
        string levelId,
        string? viewName,
        ICollection<string> warnings,
        out ExportPolygon? feature,
        ViewPlan? view = null,
        double? linkZOffset = null)
    {
        feature = null;
        if (escalator is null || polygons == null || polygons.Count == 0)
        {
            return false;
        }

        if (!TryResolveFamilyUnitZoneInfo(escalator, out string familyName, out ZoneInfo zoneInfo) ||
            !string.Equals(zoneInfo.Category, "escalator", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        IReadOnlyList<Polygon2D> effectivePolygons = polygons;
        if (CurrentGeometryView != null)
        {
            Polygon2D? rect = TryCreateEscalatorBoundingRectangle(polygons);
            if (rect != null)
            {
                effectivePolygons = new List<Polygon2D> { rect };
            }
        }
        else if (_simplifyEscalatorUnits)
        {
            double viewLevelElevation = (view?.GenLevel?.Elevation ?? 0d) - (linkZOffset ?? 0d);
            Polygon2D? simplified = TryCreateEscalatorHalfRectangle(escalator, polygons, viewLevelElevation);
            if (simplified != null)
            {
                effectivePolygons = new List<Polygon2D> { simplified };
            }
        }

        effectivePolygons = ClipPolygonsToSectionBox(effectivePolygons, $"Escalator {escalator.Id.Value}", warnings);
        if (effectivePolygons.Count == 0)
        {
            return false;
        }

        feature = CreateFeature(
            sourceElement: escalator,
            polygons: effectivePolygons,
            levelId: levelId,
            zoneInfo: zoneInfo,
            sourceLabel: familyName,
            viewName: viewName,
            warnings: warnings);
        return true;
    }

    private Polygon2D? TryCreateEscalatorBoundingRectangle(IReadOnlyList<Polygon2D> polygons)
    {
        if (!TryComputeMinAreaRectangle(
                polygons,
                out double centerProj,
                out double centerPerp,
                out double halfLength,
                out double halfWidth,
                out double cosA,
                out double sinA))
        {
            return null;
        }

        double longAxisCos, longAxisSin, crossCos, crossSin;
        double halfLong, halfShort;
        if ((halfLength * 2d) >= (halfWidth * 2d))
        {
            longAxisCos = cosA;
            longAxisSin = sinA;
            crossCos = -sinA;
            crossSin = cosA;
            halfLong = halfLength;
            halfShort = halfWidth;
        }
        else
        {
            longAxisCos = -sinA;
            longAxisSin = cosA;
            crossCos = cosA;
            crossSin = sinA;
            halfLong = halfWidth;
            halfShort = halfLength;
        }

        Point2D center = TransformPoint(centerProj, centerPerp, cosA, sinA);
        return BuildRectanglePolygon(
            center, halfLong, halfShort,
            longAxisCos, longAxisSin, crossCos, crossSin);
    }

    private bool TryComputeMinAreaRectangle(
        IReadOnlyList<Polygon2D> polygons,
        out double centerProj,
        out double centerPerp,
        out double halfLength,
        out double halfWidth,
        out double bestCos,
        out double bestSin)
    {
        centerProj = centerPerp = halfLength = halfWidth = 0d;
        bestCos = 1d;
        bestSin = 0d;

        if (polygons == null || polygons.Count == 0)
        {
            return false;
        }

        List<Point2D> allPoints = new();
        foreach (Polygon2D polygon in polygons)
        {
            foreach (Point2D point in polygon.ExteriorRing)
            {
                allPoints.Add(point);
            }
        }

        if (allPoints.Count < 3)
        {
            return false;
        }

        Coordinate[] coords = allPoints
            .Select(p => new Coordinate(p.X, p.Y))
            .ToArray();
        MultiPoint multiPoint = GeometryFactory.CreateMultiPointFromCoords(coords);
        Geometry hull = multiPoint.ConvexHull();

        if (hull is not Polygon hullPoly || hullPoly.NumPoints < 4)
        {
            return false;
        }

        LineString shell = hullPoly.ExteriorRing;
        int n = shell.NumPoints - 1;
        if (n < 3)
        {
            return false;
        }

        double minArea = double.MaxValue;
        double bestProjMin = 0d, bestProjMax = 0d;
        double bestPerpMin = 0d, bestPerpMax = 0d;

        for (int i = 0; i < n; i++)
        {
            Coordinate a = shell.GetCoordinateN(i);
            Coordinate b = shell.GetCoordinateN((i + 1) % n);

            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double len = Math.Sqrt((dx * dx) + (dy * dy));
            if (len < 1e-12)
            {
                continue;
            }

            double cosA = dx / len;
            double sinA = dy / len;

            double projMin = double.MaxValue;
            double projMax = double.MinValue;
            double perpMin = double.MaxValue;
            double perpMax = double.MinValue;

            for (int j = 0; j < n; j++)
            {
                Coordinate p = shell.GetCoordinateN(j);
                double proj = (p.X * cosA) + (p.Y * sinA);
                double perp = (-p.X * sinA) + (p.Y * cosA);
                if (proj < projMin) projMin = proj;
                if (proj > projMax) projMax = proj;
                if (perp < perpMin) perpMin = perp;
                if (perp > perpMax) perpMax = perp;
            }

            double area = (projMax - projMin) * (perpMax - perpMin);
            if (area < minArea)
            {
                minArea = area;
                bestProjMin = projMin;
                bestProjMax = projMax;
                bestPerpMin = perpMin;
                bestPerpMax = perpMax;
                bestCos = cosA;
                bestSin = sinA;
            }
        }

        if (minArea >= double.MaxValue)
        {
            return false;
        }

        centerProj = (bestProjMin + bestProjMax) * 0.5d;
        centerPerp = (bestPerpMin + bestPerpMax) * 0.5d;
        halfLength = (bestProjMax - bestProjMin) * 0.5d;
        halfWidth = (bestPerpMax - bestPerpMin) * 0.5d;
        return true;
    }

    private Polygon2D? TryCreateEscalatorHalfRectangle(
        FamilyInstance escalator,
        IReadOnlyList<Polygon2D> polygons,
        double viewLevelElevation)
    {
        if (polygons == null || polygons.Count == 0)
        {
            return null;
        }

        List<Point2D> allPoints = new();
        foreach (Polygon2D polygon in polygons)
        {
            foreach (Point2D point in polygon.ExteriorRing)
            {
                allPoints.Add(point);
            }
        }

        if (allPoints.Count < 3)
        {
            return null;
        }

        Coordinate[] coords = allPoints
            .Select(p => new Coordinate(p.X, p.Y))
            .ToArray();
        MultiPoint multiPoint = GeometryFactory.CreateMultiPointFromCoords(coords);
        Geometry hull = multiPoint.ConvexHull();

        if (hull is not Polygon hullPoly || hullPoly.NumPoints < 4)
        {
            return null;
        }

        LineString shell = hullPoly.ExteriorRing;
        int n = shell.NumPoints - 1;

        if (n < 3)
        {
            return null;
        }

        double minArea = double.MaxValue;
        double bestProjMin = 0d, bestProjMax = 0d;
        double bestPerpMin = 0d, bestPerpMax = 0d;
        double bestCos = 1d, bestSin = 0d;

        for (int i = 0; i < n; i++)
        {
            Coordinate a = shell.GetCoordinateN(i);
            Coordinate b = shell.GetCoordinateN((i + 1) % n);

            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double len = Math.Sqrt((dx * dx) + (dy * dy));
            if (len < 1e-12)
            {
                continue;
            }

            double cosA = dx / len;
            double sinA = dy / len;

            double projMin = double.MaxValue;
            double projMax = double.MinValue;
            double perpMin = double.MaxValue;
            double perpMax = double.MinValue;

            for (int j = 0; j < n; j++)
            {
                Coordinate p = shell.GetCoordinateN(j);
                double proj = (p.X * cosA) + (p.Y * sinA);
                double perp = (-p.X * sinA) + (p.Y * cosA);

                if (proj < projMin) projMin = proj;
                if (proj > projMax) projMax = proj;
                if (perp < perpMin) perpMin = perp;
                if (perp > perpMax) perpMax = perp;
            }

            double area = (projMax - projMin) * (perpMax - perpMin);
            if (area < minArea)
            {
                minArea = area;
                bestProjMin = projMin;
                bestProjMax = projMax;
                bestPerpMin = perpMin;
                bestPerpMax = perpMax;
                bestCos = cosA;
                bestSin = sinA;
            }
        }

        if (minArea >= double.MaxValue)
        {
            return null;
        }

        double projRange = bestProjMax - bestProjMin;
        double perpRange = bestPerpMax - bestPerpMin;

        double longAxisCos, longAxisSin, crossCos, crossSin;
        double halfLength, halfWidth;

        if (projRange >= perpRange)
        {
            longAxisCos = bestCos;
            longAxisSin = bestSin;
            crossCos = -bestSin;
            crossSin = bestCos;
            halfLength = projRange * 0.5d;
            halfWidth = perpRange * 0.5d;
        }
        else
        {
            longAxisCos = -bestSin;
            longAxisSin = bestCos;
            crossCos = bestCos;
            crossSin = bestSin;
            halfLength = perpRange * 0.5d;
            halfWidth = projRange * 0.5d;
        }

        double centerProj = (bestProjMin + bestProjMax) * 0.5d;
        double centerPerp = (bestPerpMin + bestPerpMax) * 0.5d;
        Point2D center = TransformPoint(centerProj, centerPerp, bestCos, bestSin);

        double lowerSideSign = ResolveLowerEndSign(
            escalator, center, longAxisCos, longAxisSin);

        if (Math.Abs(lowerSideSign) < 0.5d)
        {
            return BuildRectanglePolygon(
                center, halfLength, halfWidth,
                longAxisCos, longAxisSin, crossCos, crossSin);
        }

        bool showUpperHalf = ResolveShowUpperHalf(escalator, viewLevelElevation);
        double shiftDirection = showUpperHalf ? -lowerSideSign : lowerSideSign;
        double shift = halfLength * 0.5d * shiftDirection;
        Point2D newCenter = new(
            center.X + (shift * longAxisCos),
            center.Y + (shift * longAxisSin));
        double newHalfLength = halfLength * 0.5d;

        return BuildRectanglePolygon(
            newCenter, newHalfLength, halfWidth,
            longAxisCos, longAxisSin, crossCos, crossSin);
    }

    private bool ResolveShowUpperHalf(FamilyInstance escalator, double viewLevelElevation)
    {
        List<(Point2D Projected, double Z)> vertices = CollectEscalator3DVertices(escalator);
        if (vertices.Count == 0)
        {
            return false;
        }

        double minZ = vertices.Min(v => v.Z);
        double maxZ = vertices.Max(v => v.Z);
        double midZ = (minZ + maxZ) * 0.5d;
        return viewLevelElevation > midZ;
    }

    private double ResolveLowerEndSign(
        FamilyInstance escalator,
        Point2D center,
        double axisCos,
        double axisSin)
    {
        List<(Point2D Projected, double Z)> vertices = CollectEscalator3DVertices(escalator);
        if (vertices.Count == 0)
        {
            return 0d;
        }

        double negZSum = 0d;
        double negCount = 0d;
        double posZSum = 0d;
        double posCount = 0d;

        foreach ((Point2D projected, double z) in vertices)
        {
            double dot = (axisCos * (projected.X - center.X)) +
                         (axisSin * (projected.Y - center.Y));
            if (dot < 0)
            {
                negZSum += z;
                negCount++;
            }
            else
            {
                posZSum += z;
                posCount++;
            }
        }

        if (negCount < 1 || posCount < 1)
        {
            return 0d;
        }

        double negAvg = negZSum / negCount;
        double posAvg = posZSum / posCount;

        if (negAvg < posAvg)
        {
            return -1d;
        }

        if (posAvg < negAvg)
        {
            return 1d;
        }

        return 0d;
    }

    private List<(Point2D Projected, double Z)> CollectEscalator3DVertices(
        FamilyInstance escalator)
    {
        var result = new List<(Point2D Projected, double Z)>();

        Options options = new()
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = true,
            DetailLevel = ViewDetailLevel.Fine,
        };

        GeometryElement? geometry = escalator.get_Geometry(options);
        if (geometry == null)
        {
            return result;
        }

        List<Solid> solids = CollectSolids(geometry);
        HashSet<string> seen = new();

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

                foreach (EdgeArray edgeArray in planarFace.EdgeLoops)
                {
                    foreach (Edge edge in edgeArray)
                    {
                        IList<XYZ> tessellated = edge.AsCurve().Tessellate();
                        foreach (XYZ point in tessellated)
                        {
                            string key = $"{Math.Round(point.X, 8):R}|{Math.Round(point.Y, 8):R}|{Math.Round(point.Z, 8):R}";
                            if (seen.Add(key))
                            {
                                Point2D projected = ProjectPoint(point);
                                result.Add((projected, point.Z));
                            }
                        }
                    }
                }
            }
        }

        return result;
    }

    private static Polygon2D BuildRectanglePolygon(
        Point2D center,
        double halfLength,
        double halfWidth,
        double axisCos,
        double axisSin,
        double crossCos,
        double crossSin)
    {
        double hl = halfLength;
        double hw = halfWidth;
        double ac = axisCos;
        double ax = axisSin;
        double cc = crossCos;
        double cx = crossSin;

        List<Point2D> corners = new(5)
        {
            new Point2D(center.X - (hl * ac) - (hw * cc), center.Y - (hl * ax) - (hw * cx)),
            new Point2D(center.X + (hl * ac) - (hw * cc), center.Y + (hl * ax) - (hw * cx)),
            new Point2D(center.X + (hl * ac) + (hw * cc), center.Y + (hl * ax) + (hw * cx)),
            new Point2D(center.X - (hl * ac) + (hw * cc), center.Y - (hl * ax) + (hw * cx)),
        };

        return new Polygon2D(corners);
    }

    private static Point2D TransformPoint(double proj, double perp, double cosA, double sinA)
    {
        double x = (proj * cosA) - (perp * sinA);
        double y = (proj * sinA) + (perp * cosA);
        return new Point2D(x, y);
    }

    internal bool TryResolveEscalatorVisibility(
        FamilyInstance escalator,
        ViewPlan? view,
        ICollection<string> warnings,
        out VerticalCirculationVisibilityResult result)
    {
        result = null!;
        if (escalator is null)
        {
            return false;
        }

        List<Polygon2D> viewPolygons;
        VerticalCirculationVisibilitySourceKind sourceKind;

        if (view != null &&
            ReferenceEquals(view.Document, _document) &&
            TryExtractElementPolygonsInView(escalator, view, out viewPolygons) &&
            viewPolygons.Count > 0)
        {
            sourceKind = VerticalCirculationVisibilitySourceKind.ViewGeometry;
        }
        else if (TryExtractElementPolygons(escalator, out viewPolygons) && viewPolygons.Count > 0)
        {
            warnings.Add($"Escalator {escalator.Id.Value} visible geometry could not be extracted from view '{(view?.Name ?? "<none>")}'. Using element geometry fallback.");
            sourceKind = VerticalCirculationVisibilitySourceKind.RawGeometry;
        }
        else
        {
            warnings.Add($"Escalator {escalator.Id.Value} geometry could not be extracted.");
            return false;
        }

        if (!TryCreateVerticalCirculationGeometry(viewPolygons, out Geometry geometry, out double area))
        {
            warnings.Add($"Escalator {escalator.Id.Value} geometry could not be converted to a valid polygon.");
            return false;
        }

        result = new VerticalCirculationVisibilityResult(
            viewPolygons,
            sourceKind,
            area,
            evidenceCount: 0,
            coveredEvidenceCount: 0,
            evidenceCoverageRatio: 0d,
            candidateCount: 1,
            maskApplied: false,
            warning: null,
            geometry,
            VerticalCirculationVisibilityEvidence.Empty,
            overCoverageArea: 0d);
        return true;
    }

    private bool TryExtractFamilyUnitPolygons(
        FamilyInstance familyInstance,
        ViewPlan? view,
        out List<Polygon2D> polygons)
    {
        polygons = null!;
        if (familyInstance == null)
        {
            return false;
        }

        if (view != null &&
            ReferenceEquals(view.Document, _document) &&
            TryExtractElementPolygonsInView(familyInstance, view, out List<Polygon2D> viewPolygons) &&
            viewPolygons.Count > 0)
        {
            polygons = viewPolygons;
            return true;
        }

        return TryExtractElementPolygons(familyInstance, out polygons);
    }

    private ExportPolygon CreateFeature(
        Element sourceElement,
        Polygon2D polygon,
        string levelId,
        ZoneInfo zoneInfo,
        string? sourceLabel,
        string? viewName,
        ICollection<string> warnings)
    {
        ExportElementMetadata metadata = _metadataProvider.GetElementMetadata(sourceElement, warnings);
        string? name = _metadataProvider.GetOptionalStringParameter(sourceElement, SharedParameterManager.ImdfNameParameterName);
        string? altName = _metadataProvider.GetOptionalStringParameter(sourceElement, SharedParameterManager.ImdfAltNameParameterName);
        return CreateFeature(metadata.ExportId, polygon, levelId, zoneInfo, name, altName, sourceElement, sourceElement.Id.Value, metadata, null, sourceLabel, viewName, warnings);
    }

    private ExportPolygon CreateFeature(
        Element sourceElement,
        IReadOnlyList<Polygon2D> polygons,
        string levelId,
        ZoneInfo zoneInfo,
        string? sourceLabel,
        string? viewName,
        ICollection<string> warnings)
    {
        ExportElementMetadata metadata = _metadataProvider.GetElementMetadata(sourceElement, warnings);
        string? name = _metadataProvider.GetOptionalStringParameter(sourceElement, SharedParameterManager.ImdfNameParameterName);
        string? altName = _metadataProvider.GetOptionalStringParameter(sourceElement, SharedParameterManager.ImdfAltNameParameterName);
        return CreateFeature(metadata.ExportId, polygons, levelId, zoneInfo, name, altName, sourceElement, sourceElement.Id.Value, metadata, null, sourceLabel, viewName, warnings);
    }

    private ExportPolygon CreateFeature(
        string id,
        Polygon2D polygon,
        string levelId,
        ZoneInfo zoneInfo,
        string? name,
        string? altName,
        Element? sourceElement,
        long? sourceElementId,
        ExportElementMetadata? metadata,
        ResolvedMappingCategory? resolvedCategory = null,
        string? sourceLabel = null,
        string? viewName = null,
        ICollection<string>? warnings = null)
    {
        Point2D centroid = DisplayPointCalculator.CalculateCentroid(polygon);
        string displayPoint = DisplayPointCalculator.ToWktPoint(centroid);

        Dictionary<string, object?> attributes = new()
        {
            ["id"] = id,
            ["category"] = zoneInfo.Category,
            ["restrict"] = ImdfRestrictionNormalizer.NormalizeUnitRestriction(zoneInfo.Restriction),
            ["name"] = name,
            ["alt_name"] = altName,
            ["level_id"] = levelId,
            ["source"] = _source,
            ["display_point"] = displayPoint,
            ["source_element_id"] = sourceElementId,
            ["preview_fill_color"] = $"#{_paletteResolver.ResolveFillColor(zoneInfo.Category, zoneInfo.FillColor)}",
            ["source_label"] = sourceLabel,
        };
        AddSourceMetadata(attributes, metadata);
        AddResolvedCategoryAttributes(attributes, resolvedCategory);
        SchemaAttributeMapper.ApplyMappings(
            _schemaProfile,
            SchemaLayerType.Unit,
            attributes,
            sourceElement,
            viewName,
            warnings ?? new List<string>());

        return new ExportPolygon(polygon, attributes);
    }

    private ExportPolygon CreateFeature(
        string id,
        IReadOnlyList<Polygon2D> polygons,
        string levelId,
        ZoneInfo zoneInfo,
        string? name,
        string? altName,
        Element? sourceElement,
        long? sourceElementId,
        ExportElementMetadata? metadata,
        ResolvedMappingCategory? resolvedCategory = null,
        string? sourceLabel = null,
        string? viewName = null,
        ICollection<string>? warnings = null)
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

        Dictionary<string, object?> attributes = new()
        {
            ["id"] = id,
            ["category"] = zoneInfo.Category,
            ["restrict"] = ImdfRestrictionNormalizer.NormalizeUnitRestriction(zoneInfo.Restriction),
            ["name"] = name,
            ["alt_name"] = altName,
            ["level_id"] = levelId,
            ["source"] = _source,
            ["display_point"] = displayPoint,
            ["source_element_id"] = sourceElementId,
            ["preview_fill_color"] = $"#{_paletteResolver.ResolveFillColor(zoneInfo.Category, zoneInfo.FillColor)}",
            ["source_label"] = sourceLabel,
        };
        AddSourceMetadata(attributes, metadata);
        AddResolvedCategoryAttributes(attributes, resolvedCategory);
        SchemaAttributeMapper.ApplyMappings(
            _schemaProfile,
            SchemaLayerType.Unit,
            attributes,
            sourceElement,
            viewName,
            warnings ?? new List<string>());

        return new ExportPolygon(polygons, attributes);
    }

    private void AddSourceMetadata(IDictionary<string, object?> attributes, ExportElementMetadata? metadata)
    {
        if (attributes is null)
        {
            throw new ArgumentNullException(nameof(attributes));
        }

        if (metadata == null)
        {
            attributes["is_linked_source"] = _sourceDescriptor.IsLinkedSource;
            attributes["source_link_instance_id"] = _sourceDescriptor.LinkInstanceId;
            attributes["source_link_instance_name"] = _sourceDescriptor.LinkInstanceName;
            return;
        }

        attributes["source_document_key"] = metadata.SourceDocumentKey;
        attributes["source_document_name"] = metadata.SourceDocumentName;
        attributes["has_persisted_export_id"] = metadata.HasPersistedId;
        attributes["is_linked_source"] = _sourceDescriptor.IsLinkedSource;
        attributes["source_link_instance_id"] = _sourceDescriptor.LinkInstanceId;
        attributes["source_link_instance_name"] = _sourceDescriptor.LinkInstanceName;
    }

    private static void AddResolvedCategoryAttributes(
        IDictionary<string, object?> attributes,
        ResolvedMappingCategory? resolvedCategory)
    {
        if (attributes is null)
        {
            throw new ArgumentNullException(nameof(attributes));
        }

        if (resolvedCategory == null)
        {
            return;
        }

        bool isFloor = string.Equals(resolvedCategory.SourceKind, "floor", StringComparison.OrdinalIgnoreCase);
        bool isRoom = string.Equals(resolvedCategory.SourceKind, "room", StringComparison.OrdinalIgnoreCase);
        attributes["is_floor_derived"] = isFloor;
        attributes["is_room_derived"] = isRoom;
        attributes["assignment_source_kind"] = resolvedCategory.SourceKind;
        attributes["assignment_mapping_key"] = resolvedCategory.MappingKey;
        attributes["assignment_parsed_candidate"] = resolvedCategory.ParsedCandidate;
        attributes["assignment_parameter_name"] = resolvedCategory.ParameterName;
        attributes["source_floor_type_name"] = isFloor ? resolvedCategory.MappingKey : null;
        attributes["parsed_zone_candidate"] = resolvedCategory.ParsedCandidate;
        attributes["is_unassigned"] = resolvedCategory.IsUnassigned;
        attributes["category_resolution_source"] = resolvedCategory.ResolutionSource.ToString();
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
        ViewPlan? view,
        ICollection<string> warnings,
        out IReadOnlyList<Polygon2D> polygons)
    {
        return _stairVisibilityResolver.TryExtractVisibleStairPolygons(stairs, view, warnings, out polygons);
    }




    private bool TryExtractRoomPolygons(Room room, out List<Polygon2D> polygons)
    {
        polygons = null!;
        SpatialElementBoundaryOptions options = new()
        {
            SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish,
            StoreFreeBoundaryFaces = false,
        };

        IList<IList<BoundarySegment>>? loops = room.GetBoundarySegments(options);
        if (loops == null || loops.Count == 0)
        {
            return false;
        }

        List<List<XYZ>> boundaryLoops = new();
        foreach (IList<BoundarySegment> loop in loops)
        {
            List<XYZ> points = new();
            foreach (BoundarySegment segment in loop)
            {
                Curve? curve = segment.GetCurve();
                if (curve == null)
                {
                    continue;
                }

                IList<XYZ> tessellated = curve.Tessellate();
                if (tessellated == null || tessellated.Count == 0)
                {
                    continue;
                }

                if (points.Count > 0)
                {
                    tessellated = tessellated.Skip(1).ToList();
                }

                points.AddRange(tessellated);
            }

            if (points.Count >= 3)
            {
                if (!points[0].IsAlmostEqualTo(points[points.Count - 1]))
                {
                    points.Add(points[0]);
                }

                boundaryLoops.Add(points);
            }
        }

        if (boundaryLoops.Count == 0)
        {
            return false;
        }

        return BuildPolygonsFromLoops(boundaryLoops, out polygons);
    }

    private static string GetRoomCategoryValue(Room room, string roomCategoryParameterName)
    {
        string normalizedParameterName = string.IsNullOrWhiteSpace(roomCategoryParameterName)
            ? "Name"
            : roomCategoryParameterName.Trim();

        if (string.Equals(normalizedParameterName, "Name", StringComparison.OrdinalIgnoreCase))
        {
            return room.Name?.Trim() ?? string.Empty;
        }

        if (string.Equals(normalizedParameterName, "Number", StringComparison.OrdinalIgnoreCase))
        {
            return room.Number?.Trim() ?? string.Empty;
        }

        Parameter? parameter = room.LookupParameter(normalizedParameterName);
        if (parameter == null)
        {
            return room.Name?.Trim() ?? string.Empty;
        }

        string? value = parameter.AsString();
        if (string.IsNullOrWhiteSpace(value))
        {
            value = parameter.AsValueString();
        }

        return string.IsNullOrWhiteSpace(value) ? room.Name?.Trim() ?? string.Empty : value.Trim();
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

    private bool TryExtractElementPolygonsInView(Element element, View view, out List<Polygon2D> polygons)
    {
        polygons = null!;
        if (element == null || view == null)
        {
            return false;
        }

        if (CurrentGeometryView != null &&
            TryExtractProjectedSolidFootprint(element, includeNonVisibleObjects: false, view, out polygons))
        {
            return true;
        }

        if (CurrentGeometryView != null &&
            TryExtractProjectedSolidFootprint(element, includeNonVisibleObjects: true, view, out polygons))
        {
            return true;
        }

        List<List<XYZ>> loops = ExtractLoopsFromSolidGeometry(
            element,
            includeNonVisibleObjects: false,
            view: view);

        if (loops.Count == 0)
        {
            loops = ExtractLoopsFromSolidGeometry(
                element,
                includeNonVisibleObjects: true,
                view: view);
        }

        if (loops.Count == 0)
        {
            return false;
        }

        return BuildPolygonsFromLoops(loops, out polygons);
    }

    private bool TryExtractElementPolygons(Element element, out List<Polygon2D> polygons)
    {
        polygons = null!;

        if (element is Floor floor)
        {
            if (CurrentGeometryView != null)
            {
                if (TryExtractProjectedSolidFootprint(element, includeNonVisibleObjects: false, view: null, out polygons))
                {
                    return true;
                }

                if (TryExtractProjectedSolidFootprint(element, includeNonVisibleObjects: true, view: null, out polygons))
                {
                    return true;
                }

                List<List<XYZ>> clippedLoops = ExtractLoopsFromSolidGeometry(element, includeNonVisibleObjects: false);
                if (clippedLoops.Count == 0)
                {
                    clippedLoops = ExtractLoopsFromSolidGeometry(element, includeNonVisibleObjects: true);
                }

                return clippedLoops.Count > 0 && BuildPolygonsFromLoops(clippedLoops, out polygons);
            }

            // 1. Sketch profile — authoritative user-drawn footprint and most
            // robust for complex floors with large interior voids.
            if (TryExtractFloorPolygonsFromSketch(floor, out polygons))
            {
                return true;
            }

            // 2. Bottom faces.
            List<List<XYZ>> loops = ExtractLoopsFromFloorBottomFaces(floor);

            // 3. Top faces — same projected footprint; helps thin/reversed floors.
            if (loops.Count == 0)
            {
                loops = ExtractLoopsFromFloorTopFaces(floor);
            }

            // 4. Solid geometry (visible objects only).
            if (loops.Count == 0)
            {
                loops = ExtractLoopsFromSolidGeometry(element, includeNonVisibleObjects: false);
            }

            // 5. Solid geometry (include non-visible objects).
            if (loops.Count == 0)
            {
                loops = ExtractLoopsFromSolidGeometry(element, includeNonVisibleObjects: true);
            }

            // 6. Last fallback to sketch (in case sketch became available after API retries).
            if (loops.Count == 0)
            {
                return TryExtractFloorPolygonsFromSketch(floor, out polygons);
            }

            return BuildPolygonsFromLoops(loops, out polygons);
        }
        else
        {
            if (CurrentGeometryView != null &&
                TryExtractProjectedSolidFootprint(element, includeNonVisibleObjects: false, view: null, out polygons))
            {
                return true;
            }

            if (CurrentGeometryView != null &&
                TryExtractProjectedSolidFootprint(element, includeNonVisibleObjects: true, view: null, out polygons))
            {
                return true;
            }

            List<List<XYZ>> loops = ExtractLoopsFromSolidGeometry(element, includeNonVisibleObjects: false);
            if (loops.Count == 0)
            {
                loops = ExtractLoopsFromSolidGeometry(element, includeNonVisibleObjects: true);
            }

            if (loops.Count == 0)
            {
                return false;
            }

            return BuildPolygonsFromLoops(loops, out polygons);
        }
    }

    private bool BuildPolygonsFromLoops(List<List<XYZ>> loops, out List<Polygon2D> polygons)
    {
        polygons = null!;

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

        polygons = ClassifyLoopsIntoPolygons(projectedLoops);
        return polygons.Count > 0;
    }

    private bool TryExtractProjectedSolidFootprint(
        Element element,
        bool includeNonVisibleObjects,
        View? view,
        out List<Polygon2D> polygons)
    {
        polygons = null!;
        View? effectiveView = (View?)CurrentGeometryView ?? view;
        if (element == null || effectiveView == null)
        {
            return false;
        }

        Options options = new()
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = includeNonVisibleObjects,
            View = effectiveView,
        };

        GeometryElement? geometry;
        try
        {
            geometry = element.get_Geometry(options);
        }
        catch (Exception)
        {
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

        SectionBoxClipping.ZRange? zRange = SectionBoxClipping.TryGetZRange(effectiveView);

        List<Geometry> triangles = new();
        foreach (Solid solid in solids)
        {
            if (solid.Volume <= 0d)
            {
                continue;
            }

            foreach (Face face in solid.Faces)
            {
                Mesh mesh;
                try
                {
                    mesh = face.Triangulate();
                }
                catch (Exception)
                {
                    continue;
                }

                for (int i = 0; i < mesh.NumTriangles; i++)
                {
                    MeshTriangle triangle = mesh.get_Triangle(i);
                    XYZ v0 = triangle.get_Vertex(0);
                    XYZ v1 = triangle.get_Vertex(1);
                    XYZ v2 = triangle.get_Vertex(2);

                    if (zRange.HasValue)
                    {
                        List<XYZ[]> clippedTriangles = SectionBoxClipping.ClipTriangleToZRange(v0, v1, v2, zRange.Value);
                        foreach (XYZ[] clipped in clippedTriangles)
                        {
                            if (TryCreateProjectedTriangleGeometry(clipped[0], clipped[1], clipped[2], out Geometry? cg) && cg != null)
                            {
                                triangles.Add(cg);
                            }
                        }
                    }
                    else if (TryCreateProjectedTriangleGeometry(v0, v1, v2, out Geometry? triangleGeometry) &&
                             triangleGeometry != null)
                    {
                        triangles.Add(triangleGeometry);
                    }
                }
            }
        }

        Geometry? unioned = UnionProjectedTriangles(triangles);
        if (unioned == null || unioned.IsEmpty)
        {
            return false;
        }

        polygons = ExtractPolygons(unioned);
        return polygons.Count > 0;
    }

    private bool TryCreateProjectedTriangleGeometry(
        XYZ first,
        XYZ second,
        XYZ third,
        out Geometry? geometry)
    {
        geometry = null;
        Point2D a = ProjectPoint(first);
        Point2D b = ProjectPoint(second);
        Point2D c = ProjectPoint(third);
        if (ComputeTriangleArea(a, b, c) < ProjectedTriangleMinAreaSquareMeters)
        {
            return false;
        }

        Coordinate[] coordinates =
        {
            new(a.X, a.Y),
            new(b.X, b.Y),
            new(c.X, c.Y),
            new(a.X, a.Y),
        };

        try
        {
            Polygon triangle = GeometryFactory.CreatePolygon(coordinates);
            geometry = triangle.IsValid ? triangle : triangle.Buffer(0d);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (TopologyException)
        {
            return false;
        }

        return geometry != null &&
            !geometry.IsEmpty &&
            geometry.Area >= ProjectedTriangleMinAreaSquareMeters;
    }

    private static double ComputeTriangleArea(Point2D a, Point2D b, Point2D c)
    {
        return Math.Abs(((b.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (b.Y - a.Y))) * 0.5d;
    }

    private static Geometry? UnionProjectedTriangles(IReadOnlyList<Geometry> triangles)
    {
        if (triangles == null || triangles.Count == 0)
        {
            return null;
        }

        try
        {
            return triangles.Count == 1
                ? triangles[0].Buffer(0d)
                : UnaryUnionOp.Union(triangles).Buffer(0d);
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException)
        {
            try
            {
                GeometryPrecisionReducer reducer = new(new PrecisionModel(100_000d));
                List<Geometry> reduced = triangles.Select(reducer.Reduce).ToList();
                return reduced.Count == 1
                    ? reduced[0].Buffer(0d)
                    : UnaryUnionOp.Union(reduced).Buffer(0d);
            }
            catch (Exception reducedEx) when (reducedEx is TopologyException || reducedEx is ArgumentException)
            {
                return null;
            }
        }
    }

    private Geometry? TryGetSectionBoxFootprintPolygon()
    {
        View3D? view3D = CurrentGeometryView;
        if (view3D == null || !view3D.IsSectionBoxActive)
        {
            return null;
        }

        BoundingBoxXYZ? box;
        try
        {
            box = view3D.GetSectionBox();
        }
        catch (Exception)
        {
            return null;
        }

        if (box == null)
        {
            return null;
        }

        Transform t = box.Transform ?? Transform.Identity;
        XYZ[] corners =
        {
            t.OfPoint(new XYZ(box.Min.X, box.Min.Y, box.Min.Z)),
            t.OfPoint(new XYZ(box.Max.X, box.Min.Y, box.Min.Z)),
            t.OfPoint(new XYZ(box.Max.X, box.Max.Y, box.Min.Z)),
            t.OfPoint(new XYZ(box.Min.X, box.Max.Y, box.Min.Z)),
            t.OfPoint(new XYZ(box.Min.X, box.Min.Y, box.Max.Z)),
            t.OfPoint(new XYZ(box.Max.X, box.Min.Y, box.Max.Z)),
            t.OfPoint(new XYZ(box.Max.X, box.Max.Y, box.Max.Z)),
            t.OfPoint(new XYZ(box.Min.X, box.Max.Y, box.Max.Z)),
        };

        List<Point2D> xyPoints = corners
            .Select(c => ProjectPoint(c))
            .Distinct()
            .ToList();

        if (xyPoints.Count < 3)
        {
            return null;
        }

        List<Point2D> hull = ComputeConvexHull(xyPoints);
        if (hull.Count < 3)
        {
            return null;
        }

        Coordinate[] coords = hull
            .Select(p => new Coordinate(p.X, p.Y))
            .ToArray();

        if (coords.Length < 3)
        {
            return null;
        }

        Coordinate[] ringCoords = new Coordinate[coords.Length + 1];
        Array.Copy(coords, ringCoords, coords.Length);
        ringCoords[coords.Length] = coords[0];

        try
        {
            LinearRing ring = GeometryFactory.CreateLinearRing(ringCoords);
            Polygon polygon = GeometryFactory.CreatePolygon(ring);
            return polygon.IsValid ? polygon : polygon.Buffer(0d);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<Point2D> ComputeConvexHull(List<Point2D> points)
    {
        if (points.Count <= 3)
        {
            return points.ToList();
        }

        List<Point2D> sorted = points
            .OrderBy(p => p.X)
            .ThenBy(p => p.Y)
            .ToList();

        List<Point2D> lower = new();
        foreach (Point2D p in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0)
            {
                lower.RemoveAt(lower.Count - 1);
            }
            lower.Add(p);
        }

        List<Point2D> upper = new();
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            Point2D p = sorted[i];
            while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0)
            {
                upper.RemoveAt(upper.Count - 1);
            }
            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static double Cross(Point2D o, Point2D a, Point2D b)
    {
        return ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
    }

    private List<Polygon2D> ClipPolygonsToSectionBox(
        IReadOnlyList<Polygon2D> polygons,
        string contextLabel,
        ICollection<string> warnings)
    {
        if (CurrentGeometryView == null || polygons.Count == 0)
        {
            return polygons.ToList();
        }

        Geometry? footprint = TryGetSectionBoxFootprintPolygon();
        if (footprint == null || footprint.IsEmpty)
        {
            return polygons.ToList();
        }

        Geometry? sourceGeometry = UnionPolygonsToNtsGeometry(polygons);
        if (sourceGeometry == null || sourceGeometry.IsEmpty)
        {
            return polygons.ToList();
        }

        try
        {
            Geometry? clipped = IntersectSafe(sourceGeometry, footprint);
            if (clipped == null || clipped.IsEmpty)
            {
                warnings.Add($"{contextLabel} was fully outside the section box footprint.");
                return new List<Polygon2D>();
            }

            List<Polygon2D> clippedPolygons = ExtractPolygons(clipped);
            if (clippedPolygons.Count == 0)
            {
                warnings.Add($"{contextLabel} was fully outside the section box footprint.");
                return new List<Polygon2D>();
            }

            return clippedPolygons;
        }
        catch (Exception ex)
        {
            warnings.Add($"{contextLabel} section-box XY clip failed: {ex.Message}. Using unclipped geometry.");
            return polygons.ToList();
        }
    }

    private bool TryExtractFloorPolygonsFromSketch(Floor floor, out List<Polygon2D> polygons)
    {
        polygons = null!;
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

        polygons = ClassifyLoopsIntoPolygons(projectedLoops);
        return polygons.Count > 0;
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

    private List<List<XYZ>> ExtractLoopsFromSolidGeometry(
        Element element,
        bool includeNonVisibleObjects,
        View? view = null)
    {
        View? effectiveView = (View?)CurrentGeometryView ?? view;
        Options options = new()
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = includeNonVisibleObjects,
        };
        if (effectiveView != null)
        {
            options.View = effectiveView;
        }
        else
        {
            options.DetailLevel = ViewDetailLevel.Fine;
        }

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

        List<List<XYZ>> allLoops = new();
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

                allLoops.AddRange(ExtractLoopsFromFace(planarFace));
            }
        }

        return allLoops;
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
        XYZ hostPoint = _sourceDescriptor.TransformToHost.OfPoint(point);
        return _sharedCoordinateProjector.ProjectPoint(hostPoint);
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


    private static Geometry? ToNtsGeometry(Polygon2D polygon)
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
            return healed;
        }

        return created;
    }

    private static void AddPolygonGeometryParts(ICollection<Geometry> target, Geometry geometry)
    {
        if (geometry == null || geometry.IsEmpty)
        {
            return;
        }

        switch (geometry)
        {
            case Polygon polygon:
                target.Add(polygon);
                break;
            case MultiPolygon multiPolygon:
                for (int i = 0; i < multiPolygon.NumGeometries; i++)
                {
                    AddPolygonGeometryParts(target, multiPolygon.GetGeometryN(i));
                }

                break;
            case GeometryCollection collection:
                for (int i = 0; i < collection.NumGeometries; i++)
                {
                    AddPolygonGeometryParts(target, collection.GetGeometryN(i));
                }

                break;
        }
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

    private static List<Polygon2D> ClassifyLoopsIntoPolygons(List<List<Point2D>> projectedLoops)
    {
        // Build shell-only NTS polygons for each loop to leverage spatial containment checks.
        var loopPolygons = new List<(List<Point2D> Points, Polygon NtsPolygon, double Area)>();
        foreach (List<Point2D> loop in projectedLoops)
        {
            if (!TryCreateLinearRing(loop, out LinearRing? ring) || ring == null)
            {
                continue;
            }

            Polygon ntsPoly = GeometryFactory.CreatePolygon(ring);
            if (ntsPoly.IsEmpty)
            {
                continue;
            }

            loopPolygons.Add((loop, ntsPoly, ntsPoly.Area));
        }

        if (loopPolygons.Count == 0)
        {
            return new List<Polygon2D>();
        }

        // Classify each loop: if no larger loop contains its interior point, it's an exterior.
        int count = loopPolygons.Count;
        var isExterior = new bool[count];
        var parentIndex = new int[count]; // index of the smallest containing exterior, or -1

        for (int i = 0; i < count; i++)
        {
            parentIndex[i] = -1;
        }

        for (int i = 0; i < count; i++)
        {
            NetTopologySuite.Geometries.Point interiorPoint = loopPolygons[i].NtsPolygon.InteriorPoint;
            bool contained = false;
            for (int j = 0; j < count; j++)
            {
                if (j == i)
                {
                    continue;
                }

                if (loopPolygons[j].Area > loopPolygons[i].Area &&
                    loopPolygons[j].NtsPolygon.Contains(interiorPoint))
                {
                    contained = true;
                    // Track the smallest containing loop as potential parent.
                    if (parentIndex[i] == -1 || loopPolygons[j].Area < loopPolygons[parentIndex[i]].Area)
                    {
                        parentIndex[i] = j;
                    }
                }
            }

            if (!contained)
            {
                isExterior[i] = true;
            }
        }

        // Safety fallback: if nothing classified as exterior, use largest-area heuristic.
        if (!isExterior.Any(e => e))
        {
            int largestIndex = 0;
            for (int i = 1; i < count; i++)
            {
                if (loopPolygons[i].Area > loopPolygons[largestIndex].Area)
                {
                    largestIndex = i;
                }
            }

            isExterior[largestIndex] = true;
        }

        // Assemble polygons: each exterior collects its direct hole children.
        var result = new List<Polygon2D>();
        for (int i = 0; i < count; i++)
        {
            if (!isExterior[i])
            {
                continue;
            }

            var holes = new List<IReadOnlyList<Point2D>>();
            for (int j = 0; j < count; j++)
            {
                if (j == i || isExterior[j])
                {
                    continue;
                }

                if (parentIndex[j] == i)
                {
                    holes.Add(loopPolygons[j].Points);
                }
            }

            result.Add(new Polygon2D(loopPolygons[i].Points, holes));
        }

        return result;
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

    private static bool TryInterpolateOnPolyline(
        IReadOnlyList<Point2D> points,
        double fraction,
        out Point2D point,
        out Point2D tangent)
    {
        point = default;
        tangent = default;
        if (points == null || points.Count < 2)
        {
            return false;
        }

        double totalLength = GetPolylineLength(points);
        if (totalLength <= 1e-9d)
        {
            return false;
        }

        double clamped = Math.Max(0d, Math.Min(1d, fraction));
        double target = totalLength * clamped;
        double traversed = 0d;

        for (int i = 0; i < points.Count - 1; i++)
        {
            Point2D a = points[i];
            Point2D b = points[i + 1];
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double segmentLength = Math.Sqrt((dx * dx) + (dy * dy));
            if (segmentLength <= 1e-9d)
            {
                continue;
            }

            double next = traversed + segmentLength;
            if (target <= next || i == points.Count - 2)
            {
                double local = Math.Max(0d, Math.Min(1d, (target - traversed) / segmentLength));
                point = new Point2D(a.X + (dx * local), a.Y + (dy * local));
                tangent = new Point2D(dx / segmentLength, dy / segmentLength);
                return true;
            }

            traversed = next;
        }

        return false;
    }

    private static double GetPolylineLength(IReadOnlyList<Point2D> points)
    {
        if (points == null || points.Count < 2)
        {
            return 0d;
        }

        double length = 0d;
        for (int i = 0; i < points.Count - 1; i++)
        {
            double dx = points[i + 1].X - points[i].X;
            double dy = points[i + 1].Y - points[i].Y;
            length += Math.Sqrt((dx * dx) + (dy * dy));
        }

        return length;
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

    private bool TryResolveFamilyZoneInfo(string familyName, out ZoneInfo zoneInfo)
    {
        if (_familyCategoryOverrides.TryGetValue(familyName, out string overrideCategory) &&
            _zoneCatalog.TryGetCategoryInfo(overrideCategory, out zoneInfo))
        {
            return true;
        }

        return _zoneCatalog.TryGetFamilyInfo(familyName, out zoneInfo);
    }

    private bool TryCreateVerticalCirculationGeometry(
        IReadOnlyList<Polygon2D> polygons,
        out Geometry geometry,
        out double area)
    {
        geometry = null!;
        area = 0d;
        if (polygons == null || polygons.Count == 0)
        {
            return false;
        }

        List<Geometry> polygonGeometries = new();
        for (int i = 0; i < polygons.Count; i++)
        {
            Geometry? polygonGeometry = ToNtsGeometry(polygons[i]);
            if (polygonGeometry != null && !polygonGeometry.IsEmpty)
            {
                AddPolygonGeometryParts(polygonGeometries, polygonGeometry);
            }
        }

        if (polygonGeometries.Count == 0)
        {
            return false;
        }

        Geometry unioned;
        try
        {
            unioned = polygonGeometries.Count == 1
                ? polygonGeometries[0]
                : UnaryUnionOp.Union(polygonGeometries).Buffer(0d);
        }
        catch (TopologyException)
        {
            try
            {
                GeometryPrecisionReducer reducer = new(new PrecisionModel(100_000d));
                List<Geometry> reduced = polygonGeometries.Select(reducer.Reduce).ToList();
                unioned = reduced.Count == 1
                    ? reduced[0]
                    : UnaryUnionOp.Union(reduced).Buffer(0d);
            }
            catch (TopologyException)
            {
                return false;
            }
        }
        if (unioned.IsEmpty)
        {
            return false;
        }

        geometry = unioned;
        area = unioned.Area;
        return true;
    }

    private static bool TryResolveFloorZoneName(
        string typeName,
        out string zoneName,
        out bool prefixMatched)
    {
        zoneName = string.Empty;
        prefixMatched = false;
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        string trimmed = typeName.Trim();
        if (!TryStripFloorSuffix(trimmed, out string withoutSuffix))
        {
            return false;
        }

        if (TryStripFloorPrefix(withoutSuffix, out string withoutPrefix))
        {
            prefixMatched = true;
            zoneName = withoutPrefix.Trim();
            return zoneName.Length > 0;
        }

        zoneName = withoutSuffix.Trim();
        return zoneName.Length > 0;
    }

    private static bool TryStripFloorPrefix(string value, out string stripped)
    {
        for (int i = 0; i < FloorNamePrefixes.Length; i++)
        {
            string prefix = FloorNamePrefixes[i];
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                stripped = value.Substring(prefix.Length);
                return true;
            }
        }

        stripped = value;
        return false;
    }

    private static bool TryStripFloorSuffix(string value, out string stripped)
    {
        for (int i = 0; i < FloorNameSuffixes.Length; i++)
        {
            string suffix = FloorNameSuffixes[i];
            if (value.EndsWith(suffix, StringComparison.Ordinal))
            {
                stripped = value.Substring(0, value.Length - suffix.Length);
                return true;
            }
        }

        stripped = value;
        return false;
    }

    private static bool TryGetFloorAreaSquareMeters(Floor floor, out double areaSquareMeters)
    {
        areaSquareMeters = 0d;
        Parameter? areaParameter = floor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
        if (areaParameter == null || areaParameter.StorageType != StorageType.Double)
        {
            return false;
        }

        double squareFeet = areaParameter.AsDouble();
        if (squareFeet <= 0d)
        {
            return false;
        }

        areaSquareMeters = squareFeet * SquareFeetToSquareMeters;
        return true;
    }

    private static double ComputeTotalAreaSquareMeters(IReadOnlyList<Polygon2D> polygons)
    {
        double total = 0d;
        for (int i = 0; i < polygons.Count; i++)
        {
            total += ComputePolygonAreaSquareMeters(polygons[i]);
        }

        return total;
    }

    private static double ComputePolygonAreaSquareMeters(Polygon2D polygon)
    {
        double area = Math.Abs(GetSignedArea(polygon.ExteriorRing));
        for (int i = 0; i < polygon.InteriorRings.Count; i++)
        {
            area -= Math.Abs(GetSignedArea(polygon.InteriorRings[i]));
        }

        return Math.Max(0d, area);
    }

}
