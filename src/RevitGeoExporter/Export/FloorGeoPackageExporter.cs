using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.GeoPackage;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Export;

public sealed class FloorGeoPackageExporter
{
    private readonly Document _document;

    public FloorGeoPackageExporter(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
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

        List<string> setupWarnings = new();
        SharedParameterManager parameterManager = new(_document);
        ZoneCatalog zoneCatalog = ZoneCatalog.CreateDefault();
        ViewExportContextProvider contextProvider = new(_document);
        IReadOnlyList<ViewExportContext> contexts = contextProvider.BuildContexts(exportViews, zoneCatalog);
        EnsureSharedParameters(parameterManager, setupWarnings);
        EnsureStableIds(parameterManager, contexts, setupWarnings);

        FloorCategoryOverrideStore floorCategoryOverrideStore = new();
        string projectKey = DocumentProjectKeyBuilder.Create(_document);
        var overrideLoad = floorCategoryOverrideStore.LoadWithDiagnostics(projectKey);
        setupWarnings.AddRange(overrideLoad.Warnings);

        PersistentExportMetadataProvider metadataProvider = new(parameterManager);
        FloorExportDataPreparer preparer = new(_document, zoneCatalog, contextProvider);
        FloorExportPreparationResult prepared = preparer.PrepareViews(
            exportViews,
            featureTypes,
            metadataProvider,
            new FloorExportPreparationOptions
            {
                FloorCategoryOverrides = overrideLoad.Value,
                ViewContexts = contexts,
            });

        FloorGeoPackageExportResult result = new();
        result.AddWarnings(setupWarnings);
        result.AddWarnings(prepared.Warnings);

        int totalSteps = Math.Max(1, prepared.Views.Count * CountSelectedFeatureTypes(featureTypes));
        int completedSteps = 0;
        progressCallback?.Invoke(new ExportProgressUpdate(0, totalSteps, "Preparing export..."));

        string sourceModelName = GetSourceModelName(_document);
        string safeModelName = SanitizeFileName(sourceModelName);
        HashSet<string> usedFileStems = new(StringComparer.OrdinalIgnoreCase);
        GpkgWriter writer = new();

        foreach (PreparedViewExportData viewData in prepared.Views)
        {
            string fileStem = BuildUniqueFileStem(
                safeModelName,
                SanitizeFileName(viewData.View.Name),
                viewData.View.Id.Value,
                usedFileStems);

            if (featureTypes.HasFlag(ExportFeatureType.Unit) && viewData.UnitLayer != null)
            {
                string unitFile = Path.Combine(outputDirectory, $"{fileStem}_unit.gpkg");
                writer.Write(unitFile, targetEpsg, new[] { viewData.UnitLayer });
                result.AddViewResult(
                    new ViewExportResult(
                        viewData.View.Name,
                        viewData.Level.Name,
                        "unit",
                        unitFile,
                        viewData.UnitLayer.Features.Count));
                completedSteps++;
                progressCallback?.Invoke(
                    new ExportProgressUpdate(completedSteps, totalSteps, $"Exported {viewData.View.Name} [unit]"));
            }

            if (featureTypes.HasFlag(ExportFeatureType.Detail) && viewData.DetailLayer != null)
            {
                string detailFile = Path.Combine(outputDirectory, $"{fileStem}_detail.gpkg");
                writer.Write(detailFile, targetEpsg, new[] { viewData.DetailLayer });
                result.AddViewResult(
                    new ViewExportResult(
                        viewData.View.Name,
                        viewData.Level.Name,
                        "detail",
                        detailFile,
                        viewData.DetailLayer.Features.Count));
                completedSteps++;
                progressCallback?.Invoke(
                    new ExportProgressUpdate(completedSteps, totalSteps, $"Exported {viewData.View.Name} [detail]"));
            }

            if (featureTypes.HasFlag(ExportFeatureType.Opening) && viewData.OpeningLayer != null)
            {
                string openingFile = Path.Combine(outputDirectory, $"{fileStem}_opening.gpkg");
                writer.Write(openingFile, targetEpsg, new[] { viewData.OpeningLayer });
                result.AddViewResult(
                    new ViewExportResult(
                        viewData.View.Name,
                        viewData.Level.Name,
                        "opening",
                        openingFile,
                        viewData.OpeningLayer.Features.Count));
                completedSteps++;
                progressCallback?.Invoke(
                    new ExportProgressUpdate(completedSteps, totalSteps, $"Exported {viewData.View.Name} [opening]"));
            }

            if (featureTypes.HasFlag(ExportFeatureType.Level) && viewData.LevelLayer != null)
            {
                string levelFile = Path.Combine(outputDirectory, $"{fileStem}_level.gpkg");
                writer.Write(levelFile, targetEpsg, new[] { viewData.LevelLayer });
                result.AddViewResult(
                    new ViewExportResult(
                        viewData.View.Name,
                        viewData.Level.Name,
                        "level",
                        levelFile,
                        viewData.LevelLayer.Features.Count));
                completedSteps++;
                progressCallback?.Invoke(
                    new ExportProgressUpdate(completedSteps, totalSteps, $"Exported {viewData.View.Name} [level]"));
            }
        }

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
