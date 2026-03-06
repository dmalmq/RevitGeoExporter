using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Precision;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.GeoPackage;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Extractors;

namespace RevitGeoExporter.Export;

public sealed class FloorGeoPackageExporter
{
    // Temporary diagnostic mode to isolate floor extraction issues.
    // When enabled:
    // - only floor units are exported
    // - stairs/elevators/escalators are excluded from unit export
    // - wall-based floor splitting is disabled
    // - unit normalization/joining logic is bypassed
    private static readonly bool RawFloorOnlyDebugMode = false;

    private const double ElevatorExpansionMeters = 0.20d;
    private const double MaxUnitGapMeters = 0.15d;
    private const double MaxElevatorWalkwayProximityMeters = 0.50d;
    private const double MinEdgeLengthMeters = 0.10d;
    private const double MinimumUnitAreaSquareMeters = 0.01d;
    private const double MinVoidCoverageForVerticalFill = 0.25d;
    private const double MaxVoidToVerticalAreaRatio = 4.0d;
    private static readonly GeometryFactory GeometryFactory = new();

    private readonly Document _document;
    private readonly LevelCollector _levelCollector;
    private readonly SharedCoordinateValidator _coordinateValidator;

    public FloorGeoPackageExporter(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _levelCollector = new LevelCollector();
        _coordinateValidator = new SharedCoordinateValidator();
    }

    public FloorGeoPackageExportResult ExportSelectedViews(
        string outputDirectory,
        int targetEpsg,
        IReadOnlyList<ViewPlan> selectedViews,
        ExportFeatureType featureTypes = ExportFeatureType.All,
        Action<ExportProgressUpdate>? progressCallback = null)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        if (selectedViews is null)
        {
            throw new ArgumentNullException(nameof(selectedViews));
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
            throw new InvalidOperationException("No valid plan views were selected for export.");
        }

        Directory.CreateDirectory(outputDirectory);
        FloorGeoPackageExportResult result = new();
        List<string> warnings = new();

        SharedCoordinateValidationResult validation = _coordinateValidator.Validate(_document);
        warnings.AddRange(validation.Warnings);

        ZoneCatalog zoneCatalog = ZoneCatalog.CreateDefault();
        List<ViewExportContext> contexts = BuildViewContexts(exportViews, zoneCatalog);
        if (contexts.Count == 0)
        {
            throw new InvalidOperationException("Selected views did not contain any exportable level context.");
        }

        int featureTypeCount = CountSelectedFeatureTypes(featureTypes);
        int totalSteps = Math.Max(1, contexts.Count * featureTypeCount);
        int completedSteps = 0;
        progressCallback?.Invoke(new ExportProgressUpdate(0, totalSteps, "Preparing export..."));

        SharedParameterManager parameterManager = new(_document);
        EnsureSharedParameters(parameterManager, warnings);
        EnsureStableIds(parameterManager, contexts, warnings);

        IReadOnlyList<Level> allLevels = _levelCollector.GetAllLevels(_document);
        Dictionary<long, int> ordinalByLevelId = BuildLevelOrdinalMap(allLevels.Count > 0 ? allLevels : contexts.Select(x => x.Level).ToList());
        Dictionary<long, string> levelIdByElementId = BuildLevelIdMap(
            contexts.Select(x => x.Level).Distinct(new LevelIdComparer()).ToList(),
            parameterManager,
            warnings);

        string sourceModelName = GetSourceModelName(_document);
        string safeModelName = SanitizeFileName(sourceModelName);
        HashSet<string> usedFileStems = new(StringComparer.OrdinalIgnoreCase);

        UnitExtractor unitExtractor = new(_document, zoneCatalog, parameterManager, sourceModelName);
        DetailExtractor detailExtractor = new(_document);
        OpeningExtractor openingExtractor = new(_document, parameterManager, zoneCatalog);
        LevelBoundaryBuilder levelBoundaryBuilder = new();
        GpkgWriter writer = new();

        foreach (ViewExportContext context in contexts)
        {
            Level level = context.Level;
            if (!levelIdByElementId.TryGetValue(level.Id.Value, out string? levelId) ||
                string.IsNullOrWhiteSpace(levelId))
            {
                warnings.Add(
                    $"View '{context.View.Name}' level '{level.Name}' is missing IMDF_LevelId. Skipping view.");
                continue;
            }

            string safeViewName = SanitizeFileName(context.View.Name);
            string fileStem = BuildUniqueFileStem(
                safeModelName,
                safeViewName,
                context.View.Id.Value,
                usedFileStems);

            ExportLayer? unitLayer = null;
            List<ExportPolygon> unitFeatures = new();
            if (featureTypes.HasFlag(ExportFeatureType.Unit) || featureTypes.HasFlag(ExportFeatureType.Level))
            {
                unitLayer = LayerDefinition.CreateUnitLayer();
                AddFloorUnits(
                    levelId,
                    context.Floors,
                    context.Walls,
                    unitExtractor,
                    unitLayer,
                    warnings);

                if (!RawFloorOnlyDebugMode)
                {
                    AddStairsUnits(levelId, context.View, context.Stairs, unitExtractor, unitLayer, warnings);
                    AddFamilyUnits(levelId, context.View, context.FamilyUnits, unitExtractor, unitLayer, warnings);
                }

                List<ExportPolygon> rawUnitFeatures = unitLayer.Features.OfType<ExportPolygon>().ToList();
                unitFeatures = RawFloorOnlyDebugMode
                    ? rawUnitFeatures
                    : NormalizeUnitFeatures(rawUnitFeatures, warnings);
                unitLayer = LayerDefinition.CreateUnitLayer();
                foreach (ExportPolygon feature in unitFeatures)
                {
                    unitLayer.AddFeature(feature);
                }
            }

            if (featureTypes.HasFlag(ExportFeatureType.Unit) && unitLayer != null)
            {
                string unitFile = Path.Combine(outputDirectory, $"{fileStem}_unit.gpkg");
                writer.Write(unitFile, targetEpsg, new[] { unitLayer });
                result.AddViewResult(
                    new ViewExportResult(context.View.Name, level.Name, "unit", unitFile, unitLayer.Features.Count));
                completedSteps++;
                progressCallback?.Invoke(
                    new ExportProgressUpdate(
                        completedSteps,
                        totalSteps,
                        $"Exported {context.View.Name} [unit]"));
            }

            if (featureTypes.HasFlag(ExportFeatureType.Detail))
            {
                ExportLayer detailLayer = LayerDefinition.CreateDetailLayer();
                foreach (ExportLineString detailFeature in detailExtractor.ExtractForLevel(
                             level,
                             levelId,
                             context.DetailCurves,
                             context.Stairs,
                             warnings))
                {
                    detailLayer.AddFeature(detailFeature);
                }

                string detailFile = Path.Combine(outputDirectory, $"{fileStem}_detail.gpkg");
                writer.Write(detailFile, targetEpsg, new[] { detailLayer });
                result.AddViewResult(
                    new ViewExportResult(context.View.Name, level.Name, "detail", detailFile, detailLayer.Features.Count));
                completedSteps++;
                progressCallback?.Invoke(
                    new ExportProgressUpdate(
                        completedSteps,
                        totalSteps,
                        $"Exported {context.View.Name} [detail]"));
            }

            if (featureTypes.HasFlag(ExportFeatureType.Opening))
            {
                ExportLayer openingLayer = LayerDefinition.CreateOpeningLayer();
                foreach (ExportLineString openingFeature in openingExtractor.ExtractForLevel(
                             level,
                             levelId,
                             context.Openings,
                             context.Stairs,
                             context.FamilyUnits,
                             unitFeatures,
                             warnings))
                {
                    openingLayer.AddFeature(openingFeature);
                }

                string openingFile = Path.Combine(outputDirectory, $"{fileStem}_opening.gpkg");
                writer.Write(openingFile, targetEpsg, new[] { openingLayer });
                result.AddViewResult(
                    new ViewExportResult(context.View.Name, level.Name, "opening", openingFile, openingLayer.Features.Count));
                completedSteps++;
                progressCallback?.Invoke(
                    new ExportProgressUpdate(
                        completedSteps,
                        totalSteps,
                        $"Exported {context.View.Name} [opening]"));
            }

            if (featureTypes.HasFlag(ExportFeatureType.Level))
            {
                ExportLayer levelLayer = LayerDefinition.CreateLevelLayer();
                int ordinal = ordinalByLevelId.TryGetValue(level.Id.Value, out int computedOrdinal) ? computedOrdinal : 0;
                if (levelBoundaryBuilder.TryBuild(
                        levelId,
                        level.Name,
                        ordinal,
                        level.Elevation,
                        unitFeatures,
                        out ExportPolygon? levelBoundary) &&
                    levelBoundary != null)
                {
                    levelLayer.AddFeature(levelBoundary);
                }
                else
                {
                    warnings.Add($"Level boundary could not be derived for view '{context.View.Name}'.");
                }

                string levelFile = Path.Combine(outputDirectory, $"{fileStem}_level.gpkg");
                writer.Write(levelFile, targetEpsg, new[] { levelLayer });
                result.AddViewResult(
                    new ViewExportResult(context.View.Name, level.Name, "level", levelFile, levelLayer.Features.Count));
                completedSteps++;
                progressCallback?.Invoke(
                    new ExportProgressUpdate(
                        completedSteps,
                        totalSteps,
                        $"Exported {context.View.Name} [level]"));
            }
        }

        result.AddWarnings(warnings);
        return result;
    }

    private static int CountSelectedFeatureTypes(ExportFeatureType featureTypes)
    {
        int count = 0;
        if (featureTypes.HasFlag(ExportFeatureType.Unit))
        {
            count++;
        }

        if (featureTypes.HasFlag(ExportFeatureType.Detail))
        {
            count++;
        }

        if (featureTypes.HasFlag(ExportFeatureType.Opening))
        {
            count++;
        }

        if (featureTypes.HasFlag(ExportFeatureType.Level))
        {
            count++;
        }

        return count;
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

    private static void EnsureSharedParameters(SharedParameterManager manager, ICollection<string> warnings)
    {
        using Transaction transaction = new(manager.Document, "IMDF Export - Ensure Shared Parameters");
        transaction.Start();
        manager.EnsureParameters(warnings);
        transaction.Commit();
    }

    private static void EnsureStableIds(
        SharedParameterManager manager,
        IReadOnlyList<ViewExportContext> contexts,
        ICollection<string> warnings)
    {
        using Transaction transaction = new(manager.Document, "IMDF Export - Assign IDs");
        transaction.Start();

        IReadOnlyList<Level> levels = contexts
            .Select(context => context.Level)
            .Distinct(new LevelIdComparer())
            .ToList();
        manager.EnsureLevelIds(levels, warnings);

        Dictionary<long, Element> uniqueElements = new();
        foreach (ViewExportContext context in contexts)
        {
            AddUniqueElements(uniqueElements, context.Floors);
            AddUniqueElements(uniqueElements, context.Stairs);
            AddUniqueElements(uniqueElements, context.FamilyUnits);
            AddUniqueElements(uniqueElements, context.Openings);
        }

        manager.EnsureElementIds(uniqueElements.Values.ToList(), warnings);
        transaction.Commit();
    }

    private static void AddUniqueElements<TElement>(IDictionary<long, Element> target, IReadOnlyList<TElement> elements)
        where TElement : Element
    {
        for (int i = 0; i < elements.Count; i++)
        {
            TElement element = elements[i];
            target[element.Id.Value] = element;
        }
    }

    private static void AddFloorUnits(
        string levelId,
        IReadOnlyList<Floor> floors,
        IReadOnlyList<Wall> walls,
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
            
            // Trim vertical units (stairs, elevators, escalators) against global horizontal geometries
            // This perfectly snaps them into the floor holes mathematically!
            if (IsVerticalFillCategory(record.Category) && !globalHorizontal.IsEmpty)
            {
                Geometry trimmed = SafeOverlay(record.Geometry, globalHorizontal, (a, b) => a.Difference(b).Buffer(0d), warnings);
                converted[i] = new UnitGeometryRecord(record.Attributes, record.Category, record.IsElevator, trimmed);
            }
        }

        CloseSmallGaps(converted);

        return BuildExportPolygons(converted, warnings);
    }

    private static List<ExportPolygon> BuildExportPolygons(
        IReadOnlyList<UnitGeometryRecord> records,
        ICollection<string> warnings)
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

        // Collect all original geometries for subtraction.
        List<Geometry> originals = records.Select(r => r.Geometry).ToList();

        for (int i = 0; i < records.Count; i++)
        {
            Geometry buffered = records[i].Geometry.Buffer(halfGap);

            // Subtract every other unit's original geometry.
            for (int j = 0; j < records.Count; j++)
            {
                if (j == i) continue;
                buffered = SafeOverlay(buffered, originals[j], (a, b) => a.Difference(b));
            }

            buffered = buffered.Buffer(0d); // topology healing
            if (!buffered.IsEmpty)
            {
                UnitGeometryRecord r = records[i];
                records[i] = new UnitGeometryRecord(r.Attributes, r.Category, r.IsElevator, buffered);
            }
        }
    }


    private static Geometry GetLargestPolygon(Geometry geometry)
    {
        if (geometry is Polygon)
        {
            return geometry;
        }

        if (geometry is not GeometryCollection collection || collection.NumGeometries == 0)
        {
            return geometry;
        }

        Geometry best = Geometry.DefaultFactory.CreateEmpty(NetTopologySuite.Geometries.Dimension.Surface);
        double bestArea = 0d;
        for (int i = 0; i < collection.NumGeometries; i++)
        {
            Geometry child = collection.GetGeometryN(i);
            if (child is Polygon && child.Area > bestArea)
            {
                bestArea = child.Area;
                best = child;
            }
        }

        return best;
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

    private static Dictionary<long, string> BuildLevelIdMap(
        IReadOnlyList<Level> levels,
        SharedParameterManager parameterManager,
        ICollection<string> warnings)
    {
        Dictionary<long, string> map = new();
        foreach (Level level in levels)
        {
            string id = parameterManager.GetOrCreateLevelId(level, warnings);
            if (!string.IsNullOrWhiteSpace(id))
            {
                map[level.Id.Value] = id;
            }
        }

        return map;
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

    private static string BuildUniqueFileStem(
        string safeModelName,
        string safeViewName,
        long viewElementId,
        ISet<string> usedFileStems)
    {
        string stem = $"{safeModelName}_{safeViewName}";
        if (usedFileStems.Add(stem))
        {
            return stem;
        }

        string uniqueStem = $"{stem}_{viewElementId}";
        usedFileStems.Add(uniqueStem);
        return uniqueStem;
    }

    private static string GetSourceModelName(Document document)
    {
        string title = document.Title ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Model";
        }

        string withoutExtension = Path.GetFileNameWithoutExtension(title);
        return string.IsNullOrWhiteSpace(withoutExtension) ? title.Trim() : withoutExtension.Trim();
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unnamed";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = value;
        for (int i = 0; i < invalid.Length; i++)
        {
            sanitized = sanitized.Replace(invalid[i], '_');
        }

        return sanitized.Trim();
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

    private sealed class LevelIdComparer : IEqualityComparer<Level>
    {
        public bool Equals(Level? x, Level? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return x.Id.Value == y.Id.Value;
        }

        public int GetHashCode(Level obj)
        {
            return obj.Id.Value.GetHashCode();
        }
    }
}
