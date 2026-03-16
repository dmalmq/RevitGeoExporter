using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.GeoPackage;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Schema;
using ProjNet.CoordinateSystems;

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
        GeometryRepairOptions? geometryRepairOptions = null,
        ExportPackageOptions? packageOptions = null,
        string? profileName = null,
        string? baselineKey = null,
        CoordinateExportMode coordinateMode = CoordinateExportMode.SharedCoordinates,
        int? sourceEpsg = null,
        string? sourceCoordinateSystemId = null,
        string? sourceCoordinateSystemDefinition = null,
        UnitSource unitSource = UnitSource.Floors,
        string roomCategoryParameterName = "Name",
        LinkExportOptions? linkExportOptions = null,
        SchemaProfile? activeSchemaProfile = null,
        Action<ExportProgressUpdate>? progressCallback = null)
    {
        PreparedExportSession session = PrepareExport(
            outputDirectory,
            targetEpsg,
            selectedViews,
            featureTypes,
            geometryRepairOptions,
            packageOptions,
            profileName,
            baselineKey,
            coordinateMode,
            sourceEpsg,
            sourceCoordinateSystemId,
            sourceCoordinateSystemDefinition,
            unitSource,
            roomCategoryParameterName,
            linkExportOptions,
            activeSchemaProfile);
        return WritePreparedExport(session, progressCallback);
    }

    public PreparedExportSession PrepareExport(
        string outputDirectory,
        int targetEpsg,
        IReadOnlyList<ViewPlan> selectedViews,
        ExportFeatureType featureTypes = ExportFeatureType.All,
        GeometryRepairOptions? geometryRepairOptions = null,
        ExportPackageOptions? packageOptions = null,
        string? profileName = null,
        string? baselineKey = null,
        CoordinateExportMode coordinateMode = CoordinateExportMode.SharedCoordinates,
        int? sourceEpsg = null,
        string? sourceCoordinateSystemId = null,
        string? sourceCoordinateSystemDefinition = null,
        UnitSource unitSource = UnitSource.Floors,
        string roomCategoryParameterName = "Name",
        LinkExportOptions? linkExportOptions = null,
        SchemaProfile? activeSchemaProfile = null)
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
        string projectKey = DocumentProjectKeyBuilder.Create(_document);
        FloorCategoryOverrideStore floorCategoryOverrideStore = new();
        var overrideLoad = floorCategoryOverrideStore.LoadWithDiagnostics(projectKey);
        setupWarnings.AddRange(overrideLoad.Warnings);

        RoomCategoryOverrideStore roomCategoryOverrideStore = new();
        var roomOverrideLoad = roomCategoryOverrideStore.LoadWithDiagnostics(projectKey);
        setupWarnings.AddRange(roomOverrideLoad.Warnings);

        FamilyCategoryOverrideStore familyCategoryOverrideStore = new();
        var familyOverrideLoad = familyCategoryOverrideStore.LoadWithDiagnostics(projectKey);
        setupWarnings.AddRange(familyOverrideLoad.Warnings);

        AcceptedOpeningFamilyStore acceptedOpeningFamilyStore = new();
        var acceptedOpeningLoad = acceptedOpeningFamilyStore.LoadWithDiagnostics(projectKey);
        setupWarnings.AddRange(acceptedOpeningLoad.Warnings);
        GeometryRepairOptions effectiveGeometryRepairOptions =
            (geometryRepairOptions ?? new GeometryRepairOptions()).GetEffectiveOptions();
        ExportPackageOptions effectivePackageOptions = packageOptions ?? new ExportPackageOptions();
        LinkExportOptions effectiveLinkExportOptions = linkExportOptions?.Clone() ?? new LinkExportOptions();
        SchemaProfile effectiveSchemaProfile = activeSchemaProfile?.Clone() ?? SchemaProfile.CreateCoreProfile();

        IReadOnlyList<ViewExportContext> contexts = contextProvider.BuildContexts(
            exportViews,
            zoneCatalog,
            familyOverrideLoad.Value,
            acceptedOpeningLoad.Value,
            effectiveLinkExportOptions);
        EnsureSharedParameters(parameterManager, setupWarnings);
        EnsureStableIds(parameterManager, contexts, setupWarnings);

        PersistentExportMetadataProvider metadataProvider = new(parameterManager);
        FloorExportDataPreparer preparer = new(_document, zoneCatalog, contextProvider);
        FloorExportPreparationResult prepared = preparer.PrepareViews(
            exportViews,
            featureTypes,
            metadataProvider,
            new FloorExportPreparationOptions
            {
                FloorCategoryOverrides = overrideLoad.Value,
                RoomCategoryOverrides = roomOverrideLoad.Value,
                FamilyCategoryOverrides = familyOverrideLoad.Value,
                AcceptedOpeningFamilies = acceptedOpeningLoad.Value,
                GeometryRepairOptions = effectiveGeometryRepairOptions,
                UnitSource = unitSource,
                RoomCategoryParameterName = roomCategoryParameterName,
                LinkExportOptions = effectiveLinkExportOptions,
                ActiveSchemaProfile = effectiveSchemaProfile,
                ViewContexts = contexts,
            });

        List<string> allWarnings = new(setupWarnings.Count + prepared.Warnings.Count);
        allWarnings.AddRange(setupWarnings);
        allWarnings.AddRange(prepared.Warnings);
        FloorExportPreparationResult resultWithSetupWarnings = new(prepared.Views, allWarnings);
        string sourceModelName = GetSourceModelName(_document);
        return new PreparedExportSession(
            outputDirectory,
            targetEpsg,
            featureTypes,
            exportViews,
            contexts,
            resultWithSetupWarnings,
            overrideLoad.Value,
            roomOverrideLoad.Value,
            effectiveGeometryRepairOptions,
            effectivePackageOptions,
            profileName,
            string.IsNullOrWhiteSpace(baselineKey) ? projectKey : baselineKey!,
            projectKey,
            sourceModelName,
            coordinateMode,
            sourceEpsg,
            sourceCoordinateSystemId,
            sourceCoordinateSystemDefinition,
            unitSource,
            roomCategoryParameterName,
            effectiveLinkExportOptions,
            effectiveSchemaProfile,
            BuildIncludedLinks(contexts));
    }

    public FloorGeoPackageExportResult WritePreparedExport(
        PreparedExportSession session,
        Action<ExportProgressUpdate>? progressCallback = null)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        FloorGeoPackageExportResult result = new();
        result.AddWarnings(session.Prepared.Warnings);
        if (session.CoordinateMode == CoordinateExportMode.SharedCoordinates && session.SourceEpsg == null)
        {
            result.AddWarning("The model shared CRS could not be resolved to a numeric EPSG code. Output geometry remains in shared coordinates and uses the selected EPSG metadata value.");
        }

        CoordinateSystem? sourceCoordinateSystem = null;
        CoordinateSystem? targetCoordinateSystem = null;
        bool shouldTransform = session.CoordinateMode == CoordinateExportMode.ConvertToTargetCrs &&
                               session.SourceEpsg.GetValueOrDefault() != session.TargetEpsg;
        if (shouldTransform)
        {
            if (!CoordinateSystemCatalog.TryCreateSourceCoordinateSystem(
                session.SourceCoordinateSystemDefinition,
                session.SourceCoordinateSystemId,
                session.SourceEpsg,
                out sourceCoordinateSystem,
                out string sourceFailureReason))
            {
                throw new InvalidOperationException(sourceFailureReason);
            }

            if (!CoordinateSystemCatalog.TryCreateFromEpsg(session.TargetEpsg, out targetCoordinateSystem) || targetCoordinateSystem == null)
            {
                throw new InvalidOperationException($"Target EPSG:{session.TargetEpsg} is not supported for coordinate conversion.");
            }
        }

        int totalSteps = Math.Max(1, session.Prepared.Views.Count * CountSelectedFeatureTypes(session.FeatureTypes));
        int completedSteps = 0;
        progressCallback?.Invoke(new ExportProgressUpdate(0, totalSteps, "Preparing export..."));

        string safeModelName = SanitizeFileName(session.SourceModelName);
        HashSet<string> usedFileStems = new(StringComparer.OrdinalIgnoreCase);
        GpkgWriter writer = new();

        foreach (PreparedViewExportData viewData in session.Prepared.Views)
        {
            string fileStem = BuildUniqueFileStem(
                safeModelName,
                SanitizeFileName(viewData.View.Name),
                viewData.View.Id.Value,
                usedFileStems);

            if (session.FeatureTypes.HasFlag(ExportFeatureType.Unit) && viewData.UnitLayer != null)
            {
                ExportLayer unitLayer = PrepareLayerForWrite(viewData.UnitLayer, shouldTransform, sourceCoordinateSystem, targetCoordinateSystem);
                string unitFile = Path.Combine(session.OutputDirectory, $"{fileStem}_unit.gpkg");
                writer.Write(unitFile, session.OutputEpsg, new[] { unitLayer });
                result.AddViewResult(
                    new ViewExportResult(
                        viewData.View.Name,
                        viewData.Level.Name,
                        "unit",
                        unitFile,
                        unitLayer.Features.Count));
                completedSteps++;
                progressCallback?.Invoke(
                    new ExportProgressUpdate(completedSteps, totalSteps, $"Exported {viewData.View.Name} [unit]"));
            }

            if (session.FeatureTypes.HasFlag(ExportFeatureType.Detail) && viewData.DetailLayer != null)
            {
                ExportLayer detailLayer = PrepareLayerForWrite(viewData.DetailLayer, shouldTransform, sourceCoordinateSystem, targetCoordinateSystem);
                string detailFile = Path.Combine(session.OutputDirectory, $"{fileStem}_detail.gpkg");
                writer.Write(detailFile, session.OutputEpsg, new[] { detailLayer });
                result.AddViewResult(
                    new ViewExportResult(
                        viewData.View.Name,
                        viewData.Level.Name,
                        "detail",
                        detailFile,
                        detailLayer.Features.Count));
                completedSteps++;
                progressCallback?.Invoke(
                    new ExportProgressUpdate(completedSteps, totalSteps, $"Exported {viewData.View.Name} [detail]"));
            }

            if (session.FeatureTypes.HasFlag(ExportFeatureType.Opening) && viewData.OpeningLayer != null)
            {
                ExportLayer openingLayer = PrepareLayerForWrite(viewData.OpeningLayer, shouldTransform, sourceCoordinateSystem, targetCoordinateSystem);
                string openingFile = Path.Combine(session.OutputDirectory, $"{fileStem}_opening.gpkg");
                writer.Write(openingFile, session.OutputEpsg, new[] { openingLayer });
                result.AddViewResult(
                    new ViewExportResult(
                        viewData.View.Name,
                        viewData.Level.Name,
                        "opening",
                        openingFile,
                        openingLayer.Features.Count));
                completedSteps++;
                progressCallback?.Invoke(
                    new ExportProgressUpdate(completedSteps, totalSteps, $"Exported {viewData.View.Name} [opening]"));
            }

            if (session.FeatureTypes.HasFlag(ExportFeatureType.Level) && viewData.LevelLayer != null)
            {
                ExportLayer levelLayer = PrepareLayerForWrite(viewData.LevelLayer, shouldTransform, sourceCoordinateSystem, targetCoordinateSystem);
                string levelFile = Path.Combine(session.OutputDirectory, $"{fileStem}_level.gpkg");
                writer.Write(levelFile, session.OutputEpsg, new[] { levelLayer });
                result.AddViewResult(
                    new ViewExportResult(
                        viewData.View.Name,
                        viewData.Level.Name,
                        "level",
                        levelFile,
                        levelLayer.Features.Count));
                completedSteps++;
                progressCallback?.Invoke(
                    new ExportProgressUpdate(completedSteps, totalSteps, $"Exported {viewData.View.Name} [level]"));
            }
        }

        return result;
    }

    private static ExportLayer PrepareLayerForWrite(
        ExportLayer layer,
        bool shouldTransform,
        CoordinateSystem? sourceCoordinateSystem,
        CoordinateSystem? targetCoordinateSystem)
    {
        if (!shouldTransform)
        {
            return layer;
        }

        if (sourceCoordinateSystem == null || targetCoordinateSystem == null)
        {
            throw new InvalidOperationException("Coordinate conversion was requested, but the required CRS definitions were not available.");
        }

        return CoordinateSystemCatalog.ReprojectLayer(layer, sourceCoordinateSystem, targetCoordinateSystem);
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
            AddUniqueElements(uniqueElements, context.Rooms);
            AddUniqueElements(uniqueElements, context.Stairs);
            AddUniqueElements(uniqueElements, context.FamilyUnits);
            AddUniqueElements(uniqueElements, context.Openings);
        }

        manager.EnsureElementIds(uniqueElements.Values.ToList(), warnings);
        transaction.Commit();
    }

    private static IReadOnlyList<LinkedModelSummary> BuildIncludedLinks(IReadOnlyList<ViewExportContext> contexts)
    {
        return contexts
            .SelectMany(context => context.LinkedSources)
            .GroupBy(source => source.LinkInstance.Id.Value)
            .Select(group =>
            {
                LinkedViewSourceContext first = group.First();
                return new LinkedModelSummary(
                    first.LinkInstance.Id.Value,
                    first.LinkInstance.Name,
                    first.SourceDocumentKey,
                    first.SourceDocumentName);
            })
            .OrderBy(summary => summary.LinkInstanceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(summary => summary.LinkInstanceId)
            .ToList();
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
