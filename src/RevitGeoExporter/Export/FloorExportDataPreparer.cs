using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Precision;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Extractors;

namespace RevitGeoExporter.Export;

public sealed class FloorExportDataPreparer
{
    private static readonly bool RawFloorOnlyDebugMode = false;

    private const double MaxUnitGapMeters = 0.15d;
    private const double MinimumUnitAreaSquareMeters = 0.01d;
    private static readonly GeometryFactory GeometryFactory = new();

    private readonly Document _document;
    private readonly LevelCollector _levelCollector;
    private readonly SharedCoordinateValidator _coordinateValidator;
    private readonly ZoneCatalog _zoneCatalog;
    private readonly FloorCategoryOverrideStore _floorCategoryOverrideStore;
    private readonly string _projectKey;

    public FloorExportDataPreparer(
        Document document,
        ZoneCatalog? zoneCatalog = null,
        FloorCategoryOverrideStore? floorCategoryOverrideStore = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _levelCollector = new LevelCollector();
        _coordinateValidator = new SharedCoordinateValidator();
        _zoneCatalog = zoneCatalog ?? ZoneCatalog.CreateDefault();
        _floorCategoryOverrideStore = floorCategoryOverrideStore ?? new FloorCategoryOverrideStore();
        _projectKey = DocumentProjectKeyBuilder.Create(_document);
    }

    public FloorExportPreparationResult PrepareViews(
        IReadOnlyList<ViewPlan> selectedViews,
        ExportFeatureType featureTypes,
        IExportMetadataProvider metadataProvider)
    {
        if (selectedViews is null)
        {
            throw new ArgumentNullException(nameof(selectedViews));
        }

        if (metadataProvider is null)
        {
            throw new ArgumentNullException(nameof(metadataProvider));
        }

        if (featureTypes == ExportFeatureType.None)
        {
            throw new ArgumentException("At least one feature type must be selected.", nameof(featureTypes));
        }

        List<ViewPlan> exportViews = selectedViews
            .Where(view => view != null && view.GenLevel != null)
            .GroupBy(view => view.Id.Value)
            .Select(group => group.First())
            .ToList();
        if (exportViews.Count == 0)
        {
            throw new InvalidOperationException("No valid plan views were selected.");
        }

        List<string> warnings = new();
        SharedCoordinateValidationResult validation = _coordinateValidator.Validate(_document);
        warnings.AddRange(validation.Warnings);

        IReadOnlyDictionary<string, string> floorCategoryOverrides =
            _floorCategoryOverrideStore.Load(_projectKey);
        FloorCategoryResolver floorCategoryResolver = new(_zoneCatalog, floorCategoryOverrides);
        List<ViewExportContext> contexts = BuildViewContexts(exportViews, _zoneCatalog);
        if (contexts.Count == 0)
        {
            throw new InvalidOperationException("Selected views did not contain any exportable level context.");
        }

        IReadOnlyList<Level> allLevels = _levelCollector.GetAllLevels(_document);
        Dictionary<long, int> ordinalByLevelId = BuildLevelOrdinalMap(
            allLevels.Count > 0 ? allLevels : contexts.Select(x => x.Level).ToList());

        string sourceModelName = GetSourceModelName(_document);
        UnitExtractor unitExtractor = new(
            _document,
            _zoneCatalog,
            metadataProvider,
            sourceModelName,
            floorCategoryResolver);
        DetailExtractor detailExtractor = new(_document);
        OpeningExtractor openingExtractor = new(_document, metadataProvider, _zoneCatalog);
        LevelBoundaryBuilder levelBoundaryBuilder = new();

        List<PreparedViewExportData> preparedViews = new(contexts.Count);
        foreach (ViewExportContext context in contexts)
        {
            List<string> viewWarnings = new();
            string levelId = metadataProvider.GetLevelId(context.Level, viewWarnings);
            if (string.IsNullOrWhiteSpace(levelId))
            {
                viewWarnings.Add(
                    $"View '{context.View.Name}' level '{context.Level.Name}' is missing IMDF_LevelId. Skipping view.");
                warnings.AddRange(viewWarnings);
                continue;
            }

            List<ExportPolygon> unitFeatures = new();
            ExportLayer? unitLayer = null;
            if (NeedsUnitContext(featureTypes))
            {
                ExportLayer rawUnitLayer = LayerDefinition.CreateUnitLayer();
                AddFloorUnits(levelId, context.Floors, unitExtractor, rawUnitLayer, viewWarnings);

                if (!RawFloorOnlyDebugMode)
                {
                    AddStairsUnits(levelId, context.View, context.Stairs, unitExtractor, rawUnitLayer, viewWarnings);
                    AddFamilyUnits(levelId, context.View, context.FamilyUnits, unitExtractor, rawUnitLayer, viewWarnings);
                }

                List<ExportPolygon> rawUnitFeatures = rawUnitLayer.Features.OfType<ExportPolygon>().ToList();
                unitFeatures = RawFloorOnlyDebugMode
                    ? rawUnitFeatures
                    : NormalizeUnitFeatures(rawUnitFeatures, viewWarnings);

                unitLayer = LayerDefinition.CreateUnitLayer();
                foreach (ExportPolygon feature in unitFeatures)
                {
                    unitLayer.AddFeature(feature);
                }
            }

            ExportLayer? detailLayer = null;
            if (featureTypes.HasFlag(ExportFeatureType.Detail))
            {
                detailLayer = LayerDefinition.CreateDetailLayer();
                foreach (ExportLineString detailFeature in detailExtractor.ExtractForLevel(
                             context.Level,
                             levelId,
                             context.DetailCurves,
                             context.Stairs,
                             viewWarnings))
                {
                    detailLayer.AddFeature(detailFeature);
                }
            }

            ExportLayer? openingLayer = null;
            if (featureTypes.HasFlag(ExportFeatureType.Opening))
            {
                openingLayer = LayerDefinition.CreateOpeningLayer();
                foreach (ExportLineString openingFeature in openingExtractor.ExtractForLevel(
                             context.Level,
                             levelId,
                             context.Openings,
                             context.Stairs,
                             context.FamilyUnits,
                             unitFeatures,
                             viewWarnings))
                {
                    openingLayer.AddFeature(openingFeature);
                }
            }

            ExportLayer? levelLayer = null;
            if (featureTypes.HasFlag(ExportFeatureType.Level))
            {
                levelLayer = LayerDefinition.CreateLevelLayer();
                int ordinal = ordinalByLevelId.TryGetValue(context.Level.Id.Value, out int computedOrdinal)
                    ? computedOrdinal
                    : 0;
                if (levelBoundaryBuilder.TryBuild(
                        levelId,
                        context.Level.Name,
                        ordinal,
                        context.Level.Elevation,
                        unitFeatures,
                        out ExportPolygon? levelBoundary) &&
                    levelBoundary != null)
                {
                    levelLayer.AddFeature(levelBoundary);
                }
                else
                {
                    viewWarnings.Add($"Level boundary could not be derived for view '{context.View.Name}'.");
                }
            }

            warnings.AddRange(viewWarnings);
            preparedViews.Add(
                new PreparedViewExportData(
                    context.View,
                    context.Level,
                    levelId,
                    unitLayer,
                    detailLayer,
                    openingLayer,
                    levelLayer,
                    viewWarnings.ToList()));
        }

        return new FloorExportPreparationResult(preparedViews, warnings);
    }

    public PreparedViewExportData PrepareView(
        ViewPlan view,
        ExportFeatureType featureTypes,
        IExportMetadataProvider metadataProvider)
    {
        FloorExportPreparationResult result = PrepareViews(new[] { view }, featureTypes, metadataProvider);
        if (result.Views.Count == 0)
        {
            throw new InvalidOperationException("The selected view did not produce any prepared export data.");
        }

        return result.Views[0];
    }

    private static bool NeedsUnitContext(ExportFeatureType featureTypes)
    {
        return featureTypes.HasFlag(ExportFeatureType.Unit) ||
               featureTypes.HasFlag(ExportFeatureType.Opening) ||
               featureTypes.HasFlag(ExportFeatureType.Level);
    }

    private List<ViewExportContext> BuildViewContexts(
        IReadOnlyList<ViewPlan> selectedViews,
        ZoneCatalog zoneCatalog)
    {
        List<ViewExportContext> contexts = new(selectedViews.Count);
        foreach (ViewPlan view in selectedViews)
        {
            Level? level = view.GenLevel;
            if (level == null)
            {
                continue;
            }

            contexts.Add(
                new ViewExportContext(
                    view,
                    level,
                    CollectFloorsInView(_document, view.Id),
                    CollectWallsInView(_document, view.Id),
                    CollectStairsInView(_document, view.Id),
                    CollectFamilyUnitsInView(_document, view.Id, zoneCatalog),
                    CollectOpeningInstancesInView(_document, view.Id),
                    CollectDetailCurvesInView(_document, view.Id)));
        }

        return contexts;
    }

    private static void AddFloorUnits(
        string levelId,
        IReadOnlyList<Floor> floors,
        UnitExtractor extractor,
        ExportLayer unitLayer,
        ICollection<string> warnings)
    {
        foreach (Floor floor in floors)
        {
            if (!extractor.TryCreateFloorUnits(
                    floor,
                    levelId,
                    warnings,
                    out IReadOnlyList<ExportPolygon> features))
            {
                continue;
            }

            foreach (ExportPolygon feature in features)
            {
                unitLayer.AddFeature(feature);
            }
        }
    }

    private static void AddStairsUnits(
        string levelId,
        ViewPlan view,
        IReadOnlyList<Stairs> stairs,
        UnitExtractor extractor,
        ExportLayer unitLayer,
        ICollection<string> warnings)
    {
        foreach (Stairs stair in stairs)
        {
            if (extractor.TryCreateStairsUnit(stair, view, levelId, warnings, out ExportPolygon? feature) &&
                feature != null)
            {
                unitLayer.AddFeature(feature);
            }
        }
    }

    private static void AddFamilyUnits(
        string levelId,
        ViewPlan view,
        IReadOnlyList<FamilyInstance> familyUnits,
        UnitExtractor extractor,
        ExportLayer unitLayer,
        ICollection<string> warnings)
    {
        foreach (FamilyInstance familyUnit in familyUnits)
        {
            if (extractor.TryCreateFamilyUnit(familyUnit, view, levelId, warnings, out ExportPolygon? feature) &&
                feature != null)
            {
                unitLayer.AddFeature(feature);
            }
        }
    }

    private static List<ExportPolygon> NormalizeUnitFeatures(
        IReadOnlyList<ExportPolygon> unitFeatures,
        ICollection<string> warnings)
    {
        if (unitFeatures.Count == 0)
        {
            return new List<ExportPolygon>();
        }

        List<UnitGeometryRecord> converted = new(unitFeatures.Count);
        List<Geometry> horizontalGeometries = new();

        for (int i = 0; i < unitFeatures.Count; i++)
        {
            ExportPolygon feature = unitFeatures[i];
            Geometry geometry = ToMultiPolygonGeometry(feature);
            if (geometry.IsEmpty)
            {
                continue;
            }

            string category = GetCategory(feature);
            bool isElevator = string.Equals(category, "elevator", StringComparison.OrdinalIgnoreCase);
            UnitGeometryRecord record = new(feature.Attributes, category, isElevator, geometry);
            converted.Add(record);
            if (!IsVerticalFillCategory(category))
            {
                horizontalGeometries.Add(geometry);
            }
        }

        if (converted.Count == 0)
        {
            return new List<ExportPolygon>();
        }

        Geometry globalHorizontal = Geometry.DefaultFactory.CreateGeometryCollection(Array.Empty<Geometry>());
        if (horizontalGeometries.Count > 0)
        {
            try
            {
                globalHorizontal = UnaryUnionOp.Union(horizontalGeometries).Buffer(0d);
            }
            catch (TopologyException)
            {
                try
                {
                    GeometryPrecisionReducer reducer = new(new PrecisionModel(100_000d));
                    List<Geometry> reduced = horizontalGeometries.Select(g => reducer.Reduce(g)).ToList();
                    globalHorizontal = UnaryUnionOp.Union(reduced).Buffer(0d);
                    warnings.Add("Global horizontal union required reduced precision.");
                }
                catch (TopologyException)
                {
                    warnings.Add("Global horizontal union failed.");
                }
            }
        }

        for (int i = 0; i < converted.Count; i++)
        {
            UnitGeometryRecord record = converted[i];
            if (IsVerticalFillCategory(record.Category) && !globalHorizontal.IsEmpty)
            {
                Geometry trimmed = SafeOverlay(
                    record.Geometry,
                    globalHorizontal,
                    (a, b) => a.Difference(b).Buffer(0d),
                    warnings);
                converted[i] = new UnitGeometryRecord(record.Attributes, record.Category, record.IsElevator, trimmed);
            }
        }

        CloseSmallGaps(converted);
        return BuildExportPolygons(converted);
    }

    private static List<ExportPolygon> BuildExportPolygons(IReadOnlyList<UnitGeometryRecord> records)
    {
        List<ExportPolygon> exported = new(records.Count);
        for (int i = 0; i < records.Count; i++)
        {
            ExportPolygon? feature = ToExportPolygon(records[i].Geometry, records[i].Attributes);
            if (feature != null)
            {
                exported.Add(feature);
            }
        }

        return exported;
    }

    private static void CloseSmallGaps(List<UnitGeometryRecord> records)
    {
        double halfGap = MaxUnitGapMeters / 2d;
        List<Geometry> originals = records.Select(r => r.Geometry).ToList();

        for (int i = 0; i < records.Count; i++)
        {
            Geometry buffered = records[i].Geometry.Buffer(halfGap);
            for (int j = 0; j < records.Count; j++)
            {
                if (j == i)
                {
                    continue;
                }

                buffered = SafeOverlay(buffered, originals[j], (a, b) => a.Difference(b));
            }

            buffered = buffered.Buffer(0d);
            if (!buffered.IsEmpty)
            {
                UnitGeometryRecord current = records[i];
                records[i] = new UnitGeometryRecord(current.Attributes, current.Category, current.IsElevator, buffered);
            }
        }
    }

    private static ExportPolygon? ToExportPolygon(
        Geometry geometry,
        IReadOnlyDictionary<string, object?> attributes)
    {
        if (geometry == null || geometry.IsEmpty)
        {
            return null;
        }

        List<Polygon2D> polygons = ExtractPolygons(geometry);
        if (polygons.Count == 0)
        {
            return null;
        }

        Dictionary<string, object?> copiedAttributes = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> kvp in attributes)
        {
            copiedAttributes[kvp.Key] = kvp.Value;
        }

        return new ExportPolygon(polygons, copiedAttributes);
    }

    private static Geometry ToMultiPolygonGeometry(ExportPolygon feature)
    {
        List<Geometry> geometries = new();
        foreach (Polygon2D polygon in feature.Polygons)
        {
            Geometry? nts = ToNtsGeometry(polygon);
            if (nts != null && !nts.IsEmpty)
            {
                AddPolygonGeometryParts(geometries, nts);
            }
        }

        return geometries.Count switch
        {
            0 => GeometryFactory.CreateGeometryCollection(),
            1 => geometries[0],
            _ => UnaryUnionOp.Union(geometries).Buffer(0d),
        };
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
        return created.IsValid ? created : created.Buffer(0d);
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

    private static Geometry ToOverlayPolygonalGeometry(Geometry geometry)
    {
        if (geometry == null || geometry.IsEmpty)
        {
            return GeometryFactory.CreateGeometryCollection(Array.Empty<Geometry>());
        }

        if (geometry is Polygon || geometry is MultiPolygon)
        {
            return geometry;
        }

        List<Geometry> polygons = new();
        AddPolygonGeometryParts(polygons, geometry);
        if (polygons.Count == 0)
        {
            return GeometryFactory.CreateGeometryCollection(Array.Empty<Geometry>());
        }

        if (polygons.Count == 1)
        {
            return polygons[0];
        }

        try
        {
            return UnaryUnionOp.Union(polygons).Buffer(0d);
        }
        catch (TopologyException)
        {
            try
            {
                GeometryPrecisionReducer reducer = new(new PrecisionModel(100_000d));
                List<Geometry> reduced = polygons.Select(g => reducer.Reduce(g)).ToList();
                return UnaryUnionOp.Union(reduced).Buffer(0d);
            }
            catch (TopologyException)
            {
                return polygons[0];
            }
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
        if (polygon.IsEmpty || polygon.Area < MinimumUnitAreaSquareMeters)
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

    private static string GetCategory(ExportPolygon feature)
    {
        if (feature.Attributes.TryGetValue("category", out object? value))
        {
            return value?.ToString()?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    private static bool IsVerticalFillCategory(string category)
    {
        return string.Equals(category, "stairs", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "escalator", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, "elevator", StringComparison.OrdinalIgnoreCase);
    }

    private static List<Floor> CollectFloorsInView(Document document, ElementId viewId)
    {
        return new FilteredElementCollector(document, viewId)
            .OfClass(typeof(Floor))
            .WhereElementIsNotElementType()
            .Cast<Floor>()
            .ToList();
    }

    private static List<Stairs> CollectStairsInView(Document document, ElementId viewId)
    {
        return new FilteredElementCollector(document, viewId)
            .OfClass(typeof(Stairs))
            .WhereElementIsNotElementType()
            .Cast<Stairs>()
            .ToList();
    }

    private static List<Wall> CollectWallsInView(Document document, ElementId viewId)
    {
        return new FilteredElementCollector(document, viewId)
            .OfClass(typeof(Wall))
            .WhereElementIsNotElementType()
            .Cast<Wall>()
            .ToList();
    }

    private static List<FamilyInstance> CollectFamilyUnitsInView(
        Document document,
        ElementId viewId,
        ZoneCatalog zoneCatalog)
    {
        return new FilteredElementCollector(document, viewId)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(instance => zoneCatalog.TryGetFamilyInfo(UnitExtractor.GetFamilyName(instance), out _))
            .ToList();
    }

    private static List<FamilyInstance> CollectOpeningInstancesInView(Document document, ElementId viewId)
    {
        return new FilteredElementCollector(document, viewId)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(IsDoorOrWindow)
            .ToList();
    }

    private static List<CurveElement> CollectDetailCurvesInView(Document document, ElementId viewId)
    {
        return new FilteredElementCollector(document, viewId)
            .OfClass(typeof(CurveElement))
            .WhereElementIsNotElementType()
            .Cast<CurveElement>()
            .ToList();
    }

    private static Dictionary<long, int> BuildLevelOrdinalMap(IReadOnlyList<Level> levels)
    {
        Dictionary<long, int> ordinalByLevelId = new();
        if (levels.Count == 0)
        {
            return ordinalByLevelId;
        }

        int groundIndex = 0;
        double bestDistanceFromZero = double.MaxValue;
        for (int i = 0; i < levels.Count; i++)
        {
            double distance = Math.Abs(levels[i].Elevation);
            if (distance < bestDistanceFromZero)
            {
                bestDistanceFromZero = distance;
                groundIndex = i;
            }
        }

        for (int i = 0; i < levels.Count; i++)
        {
            ordinalByLevelId[levels[i].Id.Value] = i - groundIndex;
        }

        return ordinalByLevelId;
    }

    private static bool IsDoorOrWindow(FamilyInstance instance)
    {
        Category? category = instance.Category;
        if (category == null)
        {
            return false;
        }

        BuiltInCategory categoryId = (BuiltInCategory)(int)category.Id.Value;
        return categoryId == BuiltInCategory.OST_Doors || categoryId == BuiltInCategory.OST_Windows;
    }

    private static string GetSourceModelName(Document document)
    {
        string title = document.Title ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Model";
        }

        string withoutExtension = System.IO.Path.GetFileNameWithoutExtension(title);
        return string.IsNullOrWhiteSpace(withoutExtension) ? title.Trim() : withoutExtension.Trim();
    }

    private static Geometry SafeOverlay(
        Geometry a,
        Geometry b,
        Func<Geometry, Geometry, Geometry> operation,
        ICollection<string>? warnings = null)
    {
        try
        {
            return operation(a, b);
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException)
        {
            try
            {
                GeometryPrecisionReducer reducer = new(new PrecisionModel(100_000d));
                Geometry reducedA = reducer.Reduce(ToOverlayPolygonalGeometry(a));
                Geometry reducedB = reducer.Reduce(ToOverlayPolygonalGeometry(b));
                Geometry result = operation(reducedA, reducedB);
                warnings?.Add(
                    ex is ArgumentException
                        ? "A geometry overlay normalized GeometryCollection inputs to polygonal geometry."
                        : "A geometry overlay required reduced precision and may be slightly approximated.");
                return result;
            }
            catch (Exception reducedEx) when (reducedEx is TopologyException || reducedEx is ArgumentException)
            {
                warnings?.Add("A geometry overlay failed even with reduced precision; the original geometry was kept unchanged.");
                return a;
            }
        }
    }

    private sealed class ViewExportContext
    {
        public ViewExportContext(
            ViewPlan view,
            Level level,
            IReadOnlyList<Floor> floors,
            IReadOnlyList<Wall> walls,
            IReadOnlyList<Stairs> stairs,
            IReadOnlyList<FamilyInstance> familyUnits,
            IReadOnlyList<FamilyInstance> openings,
            IReadOnlyList<CurveElement> detailCurves)
        {
            View = view;
            Level = level;
            Floors = floors;
            Walls = walls;
            Stairs = stairs;
            FamilyUnits = familyUnits;
            Openings = openings;
            DetailCurves = detailCurves;
        }

        public ViewPlan View { get; }

        public Level Level { get; }

        public IReadOnlyList<Floor> Floors { get; }

        public IReadOnlyList<Wall> Walls { get; }

        public IReadOnlyList<Stairs> Stairs { get; }

        public IReadOnlyList<FamilyInstance> FamilyUnits { get; }

        public IReadOnlyList<FamilyInstance> Openings { get; }

        public IReadOnlyList<CurveElement> DetailCurves { get; }
    }

    private readonly struct UnitGeometryRecord
    {
        public UnitGeometryRecord(
            IReadOnlyDictionary<string, object?> attributes,
            string category,
            bool isElevator,
            Geometry geometry)
        {
            Attributes = attributes;
            Category = category;
            IsElevator = isElevator;
            Geometry = geometry;
        }

        public IReadOnlyDictionary<string, object?> Attributes { get; }

        public string Category { get; }

        public bool IsElevator { get; }

        public Geometry Geometry { get; }
    }
}
