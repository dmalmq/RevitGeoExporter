using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.GeoPackage;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Extractors;

namespace RevitGeoExporter.Export;

public sealed class FloorGeoPackageExporter
{
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
        IReadOnlyList<ViewPlan> selectedViews)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        if (selectedViews is null)
        {
            throw new ArgumentNullException(nameof(selectedViews));
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
        OpeningExtractor openingExtractor = new(_document, parameterManager);
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

            ExportLayer unitLayer = LayerDefinition.CreateUnitLayer();
            AddFloorUnits(levelId, context.Floors, unitExtractor, unitLayer, warnings);
            AddStairsUnits(levelId, context.Stairs, unitExtractor, unitLayer, warnings);
            AddFamilyUnits(levelId, context.FamilyUnits, unitExtractor, unitLayer, warnings);

            ExportLayer detailLayer = LayerDefinition.CreateDetailLayer();
            foreach (ExportLineString detailFeature in detailExtractor.ExtractForLevel(
                         level,
                         levelId,
                         context.DetailCurves,
                         warnings))
            {
                detailLayer.AddFeature(detailFeature);
            }

            ExportLayer openingLayer = LayerDefinition.CreateOpeningLayer();
            foreach (ExportLineString openingFeature in openingExtractor.ExtractForLevel(
                         level,
                         levelId,
                         context.Openings,
                         warnings))
            {
                openingLayer.AddFeature(openingFeature);
            }

            ExportLayer levelLayer = LayerDefinition.CreateLevelLayer();
            List<ExportPolygon> unitFeatures = unitLayer.Features.OfType<ExportPolygon>().ToList();
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

            string safeViewName = SanitizeFileName(context.View.Name);
            string fileStem = BuildUniqueFileStem(
                safeModelName,
                safeViewName,
                context.View.Id.Value,
                usedFileStems);

            string unitFile = Path.Combine(outputDirectory, $"{fileStem}_unit.gpkg");
            string detailFile = Path.Combine(outputDirectory, $"{fileStem}_detail.gpkg");
            string openingFile = Path.Combine(outputDirectory, $"{fileStem}_opening.gpkg");
            string levelFile = Path.Combine(outputDirectory, $"{fileStem}_level.gpkg");

            writer.Write(unitFile, targetEpsg, new[] { unitLayer });
            writer.Write(detailFile, targetEpsg, new[] { detailLayer });
            writer.Write(openingFile, targetEpsg, new[] { openingLayer });
            writer.Write(levelFile, targetEpsg, new[] { levelLayer });

            result.AddViewResult(
                new ViewExportResult(context.View.Name, level.Name, "unit", unitFile, unitLayer.Features.Count));
            result.AddViewResult(
                new ViewExportResult(context.View.Name, level.Name, "detail", detailFile, detailLayer.Features.Count));
            result.AddViewResult(
                new ViewExportResult(context.View.Name, level.Name, "opening", openingFile, openingLayer.Features.Count));
            result.AddViewResult(
                new ViewExportResult(context.View.Name, level.Name, "level", levelFile, levelLayer.Features.Count));
        }

        result.AddWarnings(warnings);
        return result;
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
        UnitExtractor extractor,
        ExportLayer unitLayer,
        ICollection<string> warnings)
    {
        foreach (Floor floor in floors)
        {
            if (extractor.TryCreateFloorUnit(floor, levelId, warnings, out ExportPolygon? feature) && feature != null)
            {
                unitLayer.AddFeature(feature);
            }
        }
    }

    private static void AddStairsUnits(
        string levelId,
        IReadOnlyList<Stairs> stairs,
        UnitExtractor extractor,
        ExportLayer unitLayer,
        ICollection<string> warnings)
    {
        foreach (Stairs stair in stairs)
        {
            if (extractor.TryCreateStairsUnit(stair, levelId, warnings, out ExportPolygon? feature) && feature != null)
            {
                unitLayer.AddFeature(feature);
            }
        }
    }

    private static void AddFamilyUnits(
        string levelId,
        IReadOnlyList<FamilyInstance> familyUnits,
        UnitExtractor extractor,
        ExportLayer unitLayer,
        ICollection<string> warnings)
    {
        foreach (FamilyInstance familyUnit in familyUnits)
        {
            if (extractor.TryCreateFamilyUnit(familyUnit, levelId, warnings, out ExportPolygon? feature) &&
                feature != null)
            {
                unitLayer.AddFeature(feature);
            }
        }
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

    private sealed class ViewExportContext
    {
        public ViewExportContext(
            ViewPlan view,
            Level level,
            IReadOnlyList<Floor> floors,
            IReadOnlyList<Stairs> stairs,
            IReadOnlyList<FamilyInstance> familyUnits,
            IReadOnlyList<FamilyInstance> openings,
            IReadOnlyList<CurveElement> detailCurves)
        {
            View = view;
            Level = level;
            Floors = floors;
            Stairs = stairs;
            FamilyUnits = familyUnits;
            Openings = openings;
            DetailCurves = detailCurves;
        }

        public ViewPlan View { get; }

        public Level Level { get; }

        public IReadOnlyList<Floor> Floors { get; }

        public IReadOnlyList<Stairs> Stairs { get; }

        public IReadOnlyList<FamilyInstance> FamilyUnits { get; }

        public IReadOnlyList<FamilyInstance> Openings { get; }

        public IReadOnlyList<CurveElement> DetailCurves { get; }
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
