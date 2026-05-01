using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Precision;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Schema;
using RevitGeoExporter.Extractors;
using NetTopologySuite.Simplify;

namespace RevitGeoExporter.Export;

public sealed class FloorExportDataPreparer
{
    private static readonly bool RawFloorOnlyDebugMode = false;
    private const double MinimumVerticalCirculationAreaSquareMeters = 0.05d;

    private static readonly GeometryFactory GeometryFactory = new();

    private readonly Document _document;
    private readonly LevelCollector _levelCollector;
    private readonly SharedCoordinateValidator _coordinateValidator;
    private readonly ZoneCatalog _zoneCatalog;
    private readonly ViewExportContextProvider _contextProvider;

    public FloorExportDataPreparer(
        Document document,
        ZoneCatalog? zoneCatalog = null,
        ViewExportContextProvider? contextProvider = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _levelCollector = new LevelCollector();
        _coordinateValidator = new SharedCoordinateValidator();
        _zoneCatalog = zoneCatalog ?? ZoneCatalog.CreateDefault();
        _contextProvider = contextProvider ?? new ViewExportContextProvider(_document);
    }

    public FloorExportPreparationResult PrepareViews(
        IReadOnlyList<ViewPlan> selectedViews,
        ExportFeatureType featureTypes,
        IExportMetadataProvider metadataProvider,
        FloorExportPreparationOptions? options = null)
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
        if (options?.InitialWarnings != null)
        {
            warnings.AddRange(options.InitialWarnings);
        }

        SharedCoordinateValidationResult validation = _coordinateValidator.Validate(_document);
        warnings.AddRange(validation.Warnings);

        IReadOnlyDictionary<string, string> floorCategoryOverrides =
            options?.FloorCategoryOverrides ?? EmptyOverrides();
        IReadOnlyDictionary<string, string> roomCategoryOverrides =
            options?.RoomCategoryOverrides ?? EmptyOverrides();
        IReadOnlyDictionary<string, string> familyCategoryOverrides =
            options?.FamilyCategoryOverrides ?? EmptyOverrides();
        IReadOnlyList<string> acceptedOpeningFamilies =
            options?.AcceptedOpeningFamilies ?? Array.Empty<string>();
        GeometryRepairOptions geometryRepairOptions =
            (options?.GeometryRepairOptions ?? new GeometryRepairOptions()).GetEffectiveOptions();
        FloorCategoryResolver floorCategoryResolver = new(_zoneCatalog, floorCategoryOverrides);
        RoomCategoryResolver roomCategoryResolver = new(_zoneCatalog, roomCategoryOverrides);
        IReadOnlyList<ViewExportContext> contexts =
            options?.ViewContexts ?? _contextProvider.BuildContexts(
                exportViews,
                _zoneCatalog,
                familyCategoryOverrides,
                acceptedOpeningFamilies,
                options?.LinkExportOptions);
        if (contexts.Count == 0)
        {
            throw new InvalidOperationException("Selected views did not contain any exportable level context.");
        }

        IReadOnlyList<Level> allLevels = _levelCollector.GetAllLevels(_document);
        Dictionary<long, int> ordinalByLevelId = BuildLevelOrdinalMap(
            allLevels.Count > 0 ? allLevels : contexts.Select(x => x.Level).ToList());
        SchemaProfile activeSchemaProfile = options?.ActiveSchemaProfile?.Clone() ?? SchemaProfile.CreateCoreProfile();
        UnitGeometrySource unitGeometrySource = UnitExportSettingsResolver.ResolveGeometrySource(
            options?.UnitSource ?? UnitSource.Floors,
            options?.UnitGeometrySource ?? UnitGeometrySource.Unset);
        UnitAttributeSource unitAttributeSource = UnitExportSettingsResolver.ResolveAttributeSource(
            options?.UnitSource ?? UnitSource.Floors,
            unitGeometrySource,
            options?.UnitAttributeSource ?? UnitAttributeSource.Unset);
        string hostSourceDocumentKey = DocumentProjectKeyBuilder.Create(_document);
        string hostSourceDocumentName = DocumentProjectKeyBuilder.CreateDisplayName(_document);

        string sourceModelName = GetSourceModelName(_document);
        ExportSourceDescriptor hostSourceDescriptor = ExportSourceDescriptor.CreateHost(_document);
        UnitExtractor unitExtractor = new(
            _document,
            _zoneCatalog,
            metadataProvider,
            sourceModelName,
            floorCategoryResolver,
            roomCategoryResolver,
            familyCategoryOverrides,
            hostSourceDescriptor,
            activeSchemaProfile,
            simplifyEscalatorUnits: options?.SimplifyEscalatorUnits == true);
        DetailExtractor detailExtractor = new(_document, geometryRepairOptions, hostSourceDescriptor, activeSchemaProfile);
        OpeningExtractor openingExtractor = new(_document, metadataProvider, _zoneCatalog, geometryRepairOptions, hostSourceDescriptor, activeSchemaProfile);
        LevelBoundaryBuilder levelBoundaryBuilder = new();

        List<PreparedViewExportData> preparedViews = new(contexts.Count);
        foreach (ViewExportContext context in contexts)
        {
            List<string> viewWarnings = new();
            GeometryRepairResult geometryRepair = new();
            ExportLevelMetadata levelMetadata = metadataProvider.GetLevelMetadata(context.Level, viewWarnings);
            string levelId = levelMetadata.ExportId;
            if (string.IsNullOrWhiteSpace(levelId))
            {
                viewWarnings.Add(
                    $"View '{context.View.Name}' level '{context.Level.Name}' is missing level_id. Skipping view.");
                warnings.AddRange(viewWarnings);
                continue;
            }

            List<ExportPolygon> unitFeatures = new();
            ExportLayer? unitLayer = null;
            ExportLayer? fixtureLayer = featureTypes.HasFlag(ExportFeatureType.Fixture)
                ? LayerDefinition.CreateFixtureLayer(activeSchemaProfile, viewWarnings)
                : null;
            Dictionary<long, VerticalCirculationVisibilityResult> hostVerticalCirculationResults = new();
            Dictionary<long, VerticalCirculationVisibilityResult> hostStairVisibilityResults = new();
            if (!RawFloorOnlyDebugMode &&
                (NeedsUnitContext(featureTypes) || featureTypes.HasFlag(ExportFeatureType.Detail)))
            {
                HostStairOcclusionContext hostVerticalCirculationContext = BuildHostStairOcclusionContext(
                    levelId,
                    context.Floors,
                    context.HostOpenings,
                    unitExtractor,
                    hostSourceDescriptor,
                    context.View.Name,
                    viewWarnings);
                hostStairVisibilityResults = BuildHostStairVisibilityResults(
                    levelId,
                    context.View,
                    context.Stairs,
                    unitExtractor,
                    hostVerticalCirculationContext,
                    viewWarnings,
                    options?.SimplifyStairUnits == true);
                Dictionary<long, VerticalCirculationVisibilityResult> hostEscalatorVisibilityResults = BuildHostEscalatorVisibilityResults(
                    levelId,
                    context.View,
                    context.FamilyUnits,
                    unitExtractor,
                    hostVerticalCirculationContext,
                    viewWarnings,
                    options?.SimplifyEscalatorUnits == true);
                hostVerticalCirculationResults = MergeVerticalCirculationResults(
                    hostStairVisibilityResults,
                    hostEscalatorVisibilityResults);
            }

            if (NeedsUnitContext(featureTypes))
            {
                bool collectFloorCandidates =
                    unitGeometrySource == UnitGeometrySource.Floors ||
                    unitAttributeSource == UnitAttributeSource.Floors ||
                    (unitAttributeSource == UnitAttributeSource.Hybrid && unitGeometrySource == UnitGeometrySource.Floors);
                bool collectRoomCandidates =
                    unitGeometrySource == UnitGeometrySource.Rooms ||
                    unitAttributeSource == UnitAttributeSource.Rooms ||
                    unitAttributeSource == UnitAttributeSource.Hybrid;

                ExportLayer rawFloorUnitLayer = LayerDefinition.CreateUnitLayer(activeSchemaProfile, viewWarnings);
                ExportLayer rawRoomUnitLayer = LayerDefinition.CreateUnitLayer(activeSchemaProfile, viewWarnings);
                ExportLayer supplementalUnitLayer = LayerDefinition.CreateUnitLayer(activeSchemaProfile, viewWarnings);

                if (collectRoomCandidates)
                {
                    AddRoomUnits(
                        levelId,
                        context.Rooms,
                        unitExtractor,
                        rawRoomUnitLayer,
                        options?.RoomCategoryParameterName ?? "Name",
                        context.View.Name,
                        viewWarnings);
                }

                if (collectFloorCandidates)
                {
                    AddFloorUnits(levelId, context.Floors, unitExtractor, rawFloorUnitLayer, context.View.Name, viewWarnings);
                }

                if (!RawFloorOnlyDebugMode)
                {
                    AddVerticalCirculationUnits(
                        hostVerticalCirculationResults.Values
                            .Select(result => result.ExportFeature)
                            .OfType<ExportPolygon>(),
                        supplementalUnitLayer);
                    HashSet<long> precomputedHostVerticalIds = new(hostVerticalCirculationResults.Keys);
                    AddFamilyUnits(
                        levelId,
                        context.View,
                        context.FamilyUnits,
                        unitExtractor,
                        supplementalUnitLayer,
                        fixtureLayer,
                        viewWarnings,
                        precomputedHostVerticalIds);
                }

                AddLinkedUnitFeatures(
                    context,
                    levelId,
                    metadataProvider,
                    floorCategoryResolver,
                    roomCategoryResolver,
                    familyCategoryOverrides,
                    options?.RoomCategoryParameterName ?? "Name",
                    activeSchemaProfile,
                    collectFloorCandidates ? rawFloorUnitLayer : null,
                    collectRoomCandidates ? rawRoomUnitLayer : null,
                    RawFloorOnlyDebugMode ? null : supplementalUnitLayer,
                    fixtureLayer,
                    viewWarnings,
                    options?.SimplifyStairUnits == true,
                    options?.SimplifyEscalatorUnits == true);

                List<ExportPolygon> rawUnitFeatures = UnitFeatureComposer.Compose(
                        rawFloorUnitLayer.Features.OfType<ExportPolygon>().ToList(),
                        rawRoomUnitLayer.Features.OfType<ExportPolygon>().ToList(),
                        unitGeometrySource,
                        unitAttributeSource)
                    .ToList();
                rawUnitFeatures.AddRange(supplementalUnitLayer.Features.OfType<ExportPolygon>());
                unitFeatures = RawFloorOnlyDebugMode
                    ? rawUnitFeatures
                    : NormalizeUnitFeatures(rawUnitFeatures, geometryRepairOptions, geometryRepair, viewWarnings);

                unitLayer = LayerDefinition.CreateUnitLayer(activeSchemaProfile, viewWarnings);
                foreach (ExportPolygon feature in unitFeatures)
                {
                    unitLayer.AddFeature(feature);
                }
            }

            ExportLayer? detailLayer = null;
            if (featureTypes.HasFlag(ExportFeatureType.Detail))
            {
                detailLayer = LayerDefinition.CreateDetailLayer(activeSchemaProfile, viewWarnings);
                foreach (ExportLineString detailFeature in detailExtractor.ExtractForLevel(
                             context.Level,
                             levelId,
                             context.DetailCurves,
                             context.Stairs,
                             geometryRepair,
                             viewWarnings,
                             context.View,
                             context.View.Name,
                             hostStairVisibilityResults))
                {
                    detailLayer.AddFeature(detailFeature);
                }

                AddLinkedDetailFeatures(context, levelId, geometryRepairOptions, geometryRepair, detailLayer, activeSchemaProfile, viewWarnings);
            }

            ExportLayer? openingLayer = null;
            if (featureTypes.HasFlag(ExportFeatureType.Opening))
            {
                openingLayer = LayerDefinition.CreateOpeningLayer(activeSchemaProfile, viewWarnings);
                foreach (ExportLineString openingFeature in openingExtractor.ExtractForLevel(
                             context.Level,
                             levelId,
                             context.Openings,
                             unitFeatures,
                             geometryRepair,
                             viewWarnings,
                             context.View.Name))
                {
                    openingLayer.AddFeature(openingFeature);
                }

                AddLinkedOpeningFeatures(
                    context,
                    levelId,
                    metadataProvider,
                    geometryRepairOptions,
                    unitFeatures,
                    geometryRepair,
                    openingLayer,
                    activeSchemaProfile,
                    viewWarnings);
            }

            ExportLayer? levelLayer = null;
            if (featureTypes.HasFlag(ExportFeatureType.Level))
            {
                levelLayer = LayerDefinition.CreateLevelLayer(activeSchemaProfile, viewWarnings);
                int ordinal = ordinalByLevelId.TryGetValue(context.Level.Id.Value, out int computedOrdinal)
                    ? computedOrdinal
                    : 0;
                if (levelBoundaryBuilder.TryBuild(
                        context.Level,
                        levelId,
                        ordinal,
                        unitFeatures,
                        hostSourceDocumentKey,
                        hostSourceDocumentName,
                        levelMetadata.HasPersistedId,
                        activeSchemaProfile,
                        context.View.Name,
                        geometryRepairOptions.LevelBoundaryGapClosingThresholdMeters,
                        geometryRepairOptions.LevelBoundaryMaxHoleSizeMeters,
                        viewWarnings,
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
                    fixtureLayer,
                    geometryRepair,
                    viewWarnings.ToList()));
        }

        return new FloorExportPreparationResult(preparedViews, warnings);
    }

    public PreparedViewExportData PrepareView(
        ViewPlan view,
        ExportFeatureType featureTypes,
        IExportMetadataProvider metadataProvider,
        FloorExportPreparationOptions? options = null)
    {
        FloorExportPreparationResult result = PrepareViews(new[] { view }, featureTypes, metadataProvider, options);
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
               featureTypes.HasFlag(ExportFeatureType.Level) ||
               featureTypes.HasFlag(ExportFeatureType.Fixture);
    }


    private static void AddRoomUnits(
        string levelId,
        IReadOnlyList<Room> rooms,
        UnitExtractor extractor,
        ExportLayer unitLayer,
        string roomCategoryParameterName,
        string? viewName,
        ICollection<string> warnings)
    {
        foreach (Room room in rooms)
        {
            if (!extractor.TryCreateRoomUnit(room, levelId, roomCategoryParameterName, viewName, warnings, out ExportPolygon? feature) ||
                feature == null)
            {
                continue;
            }

            unitLayer.AddFeature(feature);
        }
    }

    private static void AddFloorUnits(
        string levelId,
        IReadOnlyList<Floor> floors,
        UnitExtractor extractor,
        ExportLayer unitLayer,
        string? viewName,
        ICollection<string> warnings)
    {
        foreach (Floor floor in floors)
        {
            if (!extractor.TryCreateFloorUnits(
                    floor,
                    levelId,
                    viewName,
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

    private static void AddVerticalCirculationUnits(
        IEnumerable<ExportPolygon> features,
        ExportLayer unitLayer)
    {
        foreach (ExportPolygon feature in features)
        {
            unitLayer.AddFeature(feature);
        }
    }

    private static void AddFamilyUnits(
        string levelId,
        ViewPlan view,
        IReadOnlyList<FamilyInstance> familyUnits,
        UnitExtractor extractor,
        ExportLayer unitLayer,
        ExportLayer? fixtureLayer,
        ICollection<string> warnings,
        ICollection<long>? skippedSourceElementIds = null)
    {
        foreach (FamilyInstance familyUnit in familyUnits)
        {
            if (skippedSourceElementIds != null && skippedSourceElementIds.Contains(familyUnit.Id.Value))
            {
                continue;
            }

            if (extractor.TryCreateFamilyUnit(familyUnit, view, levelId, warnings, out ExportPolygon? feature, out string? resolvedCategory) &&
                feature != null)
            {
                bool isFixture = string.Equals(resolvedCategory, "fixture", StringComparison.OrdinalIgnoreCase);
                if (isFixture)
                {
                    if (fixtureLayer != null)
                    {
                        fixtureLayer.AddFeature(RemapToFixtureAttributes(feature));
                    }
                }
                else
                {
                    unitLayer.AddFeature(feature);
                }
            }
        }
    }

    private static Geometry? BuildFloorCoverageMask(
        string levelId,
        IReadOnlyList<Floor> floors,
        UnitExtractor extractor,
        string? viewName,
        ICollection<string> warnings)
    {
        if (floors.Count == 0)
        {
            return null;
        }

        List<Geometry> geometries = new();
        foreach (Floor floor in floors)
        {
            if (!extractor.TryCreateFloorUnits(floor, levelId, viewName, warnings, out IReadOnlyList<ExportPolygon> features))
            {
                continue;
            }

            for (int i = 0; i < features.Count; i++)
            {
                Geometry geometry = ToMultiPolygonGeometry(features[i]);
                if (!geometry.IsEmpty)
                {
                    geometries.Add(geometry);
                }
            }
        }

        if (geometries.Count == 0)
        {
            return null;
        }

        Geometry unioned = SafeUnion(geometries, warnings);
        return unioned.IsEmpty ? null : unioned;
    }

    private HostStairOcclusionContext BuildHostStairOcclusionContext(
        string levelId,
        IReadOnlyList<Floor> floors,
        IReadOnlyList<Opening> hostOpenings,
        UnitExtractor extractor,
        ExportSourceDescriptor hostSourceDescriptor,
        string? viewName,
        ICollection<string> warnings)
    {
        Geometry? floorCoverageMask = BuildFloorCoverageMask(levelId, floors, extractor, viewName, warnings);
        if (floorCoverageMask == null || floorCoverageMask.IsEmpty)
        {
            return new HostStairOcclusionContext(null, null, null);
        }

        if (hostOpenings.Count == 0)
        {
            return new HostStairOcclusionContext(floorCoverageMask, null, floorCoverageMask);
        }

        Geometry? openingMask = BuildOpeningCoverageMask(hostOpenings, hostSourceDescriptor, warnings);
        if (openingMask == null || openingMask.IsEmpty)
        {
            return new HostStairOcclusionContext(floorCoverageMask, null, floorCoverageMask);
        }

        Geometry occlusionMask = SafeOverlay(
            floorCoverageMask,
            openingMask,
            (a, b) => a.Difference(b).Buffer(0d),
            warnings);

        return new HostStairOcclusionContext(
            floorCoverageMask,
            openingMask,
            occlusionMask.IsEmpty ? null : occlusionMask);
    }

    private Dictionary<long, VerticalCirculationVisibilityResult> BuildHostStairVisibilityResults(
        string levelId,
        ViewPlan view,
        IReadOnlyList<Stairs> stairs,
        UnitExtractor extractor,
        HostStairOcclusionContext occlusionContext,
        ICollection<string> warnings,
        bool simplifyStairUnits)
    {
        Dictionary<long, VerticalCirculationVisibilityResult> results = new();
        foreach (Stairs stair in stairs)
        {
            if (!extractor.TryResolveStairVisibility(stair, view, warnings, out VerticalCirculationVisibilityResult? visibility))
            {
                continue;
            }

            VerticalCirculationVisibilityResult? shaftClipped = TryApplyShaftOpeningClip(
                "Stairs",
                stair.Id.Value,
                visibility,
                occlusionContext.FloorCoverageMask,
                occlusionContext.OpeningMask,
                warnings);
            VerticalCirculationVisibilityResult finalVisibility = shaftClipped ?? extractor.ApplyStairOcclusionMask(
                stair,
                visibility,
                occlusionContext.OcclusionMask,
                warnings);

            if (simplifyStairUnits)
            {
                List<Polygon2D>? simplified = extractor.TrySimplifyStairPolygons(
                    stair,
                    view.GenLevel,
                    finalVisibility.VisiblePolygons,
                    warnings);
                if (simplified == null || simplified.Count == 0)
                {
                    continue;
                }

                Geometry simplifiedGeometry = UnionPolygonsToGeometry(simplified, warnings);
                if (simplifiedGeometry == null || simplifiedGeometry.IsEmpty)
                {
                    warnings.Add($"Stairs {stair.Id.Value} simplified geometry could not be unioned; skipping.");
                    continue;
                }

                finalVisibility = new VerticalCirculationVisibilityResult(
                    simplified,
                    finalVisibility.SourceKind,
                    simplifiedGeometry.Area,
                    finalVisibility.EvidenceCount,
                    finalVisibility.CoveredEvidenceCount,
                    finalVisibility.EvidenceCoverageRatio,
                    finalVisibility.CandidateCount,
                    finalVisibility.MaskApplied,
                    finalVisibility.Warning,
                    simplifiedGeometry,
                    finalVisibility.Evidence,
                    finalVisibility.OverCoverageArea);
            }

            if (!extractor.TryCreateStairsUnit(
                    stair,
                    finalVisibility.VisiblePolygons,
                    levelId,
                    view.Name,
                    warnings,
                    out ExportPolygon? feature) ||
                feature == null)
            {
                continue;
            }

            ExportPolygon attributedFeature = ApplyVerticalCirculationVisibilityAttributes(feature, finalVisibility);
            results[stair.Id.Value] = finalVisibility.WithExportFeature(attributedFeature);
        }

        return results;
    }

    private Dictionary<long, VerticalCirculationVisibilityResult> BuildHostEscalatorVisibilityResults(
        string levelId,
        ViewPlan view,
        IReadOnlyList<FamilyInstance> familyUnits,
        UnitExtractor extractor,
        HostStairOcclusionContext occlusionContext,
        ICollection<string> warnings,
        bool simplifyEscalatorUnits)
    {
        Dictionary<long, VerticalCirculationVisibilityResult> results = new();
        foreach (FamilyInstance familyUnit in familyUnits)
        {
            if (!extractor.TryResolveFamilyUnitZoneInfo(familyUnit, out _, out ZoneInfo zoneInfo) ||
                !string.Equals(zoneInfo.Category, "escalator", StringComparison.OrdinalIgnoreCase) ||
                !extractor.TryResolveEscalatorVisibility(familyUnit, view, warnings, out VerticalCirculationVisibilityResult? visibility))
            {
                continue;
            }

            if (!extractor.TryCreateEscalatorUnit(
                    familyUnit,
                    visibility.VisiblePolygons,
                    levelId,
                    view.Name,
                    warnings,
                    out ExportPolygon? feature,
                    view) ||
                feature == null)
            {
                continue;
            }

            ExportPolygon attributedFeature = ApplyVerticalCirculationVisibilityAttributes(feature, visibility);
            results[familyUnit.Id.Value] = visibility.WithExportFeature(attributedFeature);
        }

        return results;
    }

    private Dictionary<long, ExportPolygon> BuildEscalatorVisibilityFeatures(
        string levelId,
        ViewPlan view,
        IReadOnlyList<FamilyInstance> familyUnits,
        UnitExtractor extractor,
        ICollection<string> warnings,
        bool simplifyEscalatorUnits,
        double? linkZOffset = null)
    {
        Dictionary<long, ExportPolygon> features = new();
        foreach (FamilyInstance familyUnit in familyUnits)
        {
            if (!extractor.TryResolveFamilyUnitZoneInfo(familyUnit, out _, out ZoneInfo zoneInfo) ||
                !string.Equals(zoneInfo.Category, "escalator", StringComparison.OrdinalIgnoreCase) ||
                !extractor.TryResolveEscalatorVisibility(familyUnit, view, warnings, out VerticalCirculationVisibilityResult? visibility) ||
                !extractor.TryCreateEscalatorUnit(
                    familyUnit,
                    visibility.VisiblePolygons,
                    levelId,
                    view.Name,
                    warnings,
                    out ExportPolygon? feature,
                    view,
                    linkZOffset) ||
                feature == null)
            {
                continue;
            }

            features[familyUnit.Id.Value] = ApplyVerticalCirculationVisibilityAttributes(feature, visibility);
        }

        return features;
    }

    private static Dictionary<long, VerticalCirculationVisibilityResult> MergeVerticalCirculationResults(
        IReadOnlyDictionary<long, VerticalCirculationVisibilityResult> stairs,
        IReadOnlyDictionary<long, VerticalCirculationVisibilityResult> escalators)
    {
        Dictionary<long, VerticalCirculationVisibilityResult> merged = new();
        foreach (KeyValuePair<long, VerticalCirculationVisibilityResult> kvp in stairs)
        {
            merged[kvp.Key] = kvp.Value;
        }

        foreach (KeyValuePair<long, VerticalCirculationVisibilityResult> kvp in escalators)
        {
            merged[kvp.Key] = kvp.Value;
        }

        return merged;
    }

    private Dictionary<long, ExportPolygon> BuildStairVisibilityFeatures(
        string levelId,
        ViewPlan view,
        IReadOnlyList<Stairs> stairs,
        UnitExtractor extractor,
        Geometry? stairOcclusionMask,
        ICollection<string> warnings,
        bool simplifyStairUnits)
    {
        Dictionary<long, ExportPolygon> features = new();
        foreach (Stairs stair in stairs)
        {
            ExportPolygon? feature;

            if (simplifyStairUnits)
            {
                if (!extractor.TryResolveStairVisibility(stair, view, warnings, out VerticalCirculationVisibilityResult? visibility))
                {
                    continue;
                }

                List<Polygon2D>? simplified = extractor.TrySimplifyStairPolygons(
                    stair,
                    view.GenLevel,
                    visibility.VisiblePolygons,
                    warnings);
                if (simplified == null || simplified.Count == 0)
                {
                    continue;
                }

                if (!extractor.TryCreateStairsUnit(stair, simplified, levelId, view.Name, warnings, out feature) ||
                    feature == null)
                {
                    continue;
                }
            }
            else
            {
                if (!extractor.TryCreateStairsUnit(stair, view, levelId, warnings, out feature) ||
                    feature == null)
                {
                    continue;
                }
            }

            ExportPolygon? finalFeature = ApplyLinkedStairOcclusionMask(stair, feature, stairOcclusionMask, warnings);
            if (finalFeature != null)
            {
                features[stair.Id.Value] = finalFeature;
            }
        }

        return features;
    }

    private static ExportPolygon? ApplyLinkedStairOcclusionMask(
        Stairs stair,
        ExportPolygon feature,
        Geometry? stairOcclusionMask,
        ICollection<string> warnings)
    {
        if (stairOcclusionMask == null || stairOcclusionMask.IsEmpty)
        {
            return feature;
        }

        Geometry geometry = ToMultiPolygonGeometry(feature);
        if (geometry.IsEmpty)
        {
            return null;
        }

        Geometry visible = SafeOverlay(
            geometry,
            stairOcclusionMask,
            (a, b) => a.Difference(b).Buffer(0d),
            warnings);

        if (visible.IsEmpty)
        {
            warnings.Add(
                $"Stairs {stair.Id.Value} floor occlusion removed the entire visible stair footprint. Keeping the stair footprint from the export view.");
            return feature;
        }

        ExportPolygon? clipped = ToExportPolygon(
            visible,
            feature.Attributes,
            0d,
            0d,
            new GeometryRepairResult());
        if (clipped != null)
        {
            return clipped;
        }

        warnings.Add(
            $"Stairs {stair.Id.Value} floor occlusion produced invalid stair geometry. Keeping the stair footprint from the export view.");
        return feature;
    }

    private static Geometry? BuildOpeningCoverageMask(
        IReadOnlyList<Opening> hostOpenings,
        ExportSourceDescriptor hostSourceDescriptor,
        ICollection<string> warnings)
    {
        if (hostOpenings.Count == 0)
        {
            return null;
        }

        SharedCoordinateProjector projector = new(hostSourceDescriptor.ProjectionProjectLocation);
        List<Geometry> geometries = new();
        foreach (Opening opening in hostOpenings)
        {
            if (TryExtractOpeningCoverageGeometry(opening, projector, out Geometry? geometry) &&
                geometry != null &&
                !geometry.IsEmpty)
            {
                geometries.Add(geometry);
            }
            else
            {
                warnings.Add($"Opening {opening.Id.Value} coverage could not be extracted for stair visibility.");
            }
        }

        if (geometries.Count == 0)
        {
            return null;
        }

        Geometry unioned = SafeUnion(geometries, warnings);
        return unioned.IsEmpty ? null : unioned;
    }

    private static bool TryExtractOpeningCoverageGeometry(
        Opening opening,
        SharedCoordinateProjector projector,
        out Geometry? geometry)
    {
        geometry = null;
        if (opening == null)
        {
            return false;
        }

        if (TryExtractOpeningCoverageGeometryFromSketch(opening, projector, out Geometry? sketchGeometry) &&
            sketchGeometry != null &&
            !sketchGeometry.IsEmpty)
        {
            geometry = sketchGeometry;
            return true;
        }

        CurveArray? boundaryCurves = opening.BoundaryCurves;
        if (boundaryCurves != null &&
            TryCreatePolygonFromCurveArray(boundaryCurves, projector, out Polygon2D polygonFromCurves))
        {
            geometry = ToMultiPolygonGeometry(new ExportPolygon(new[] { polygonFromCurves }, new Dictionary<string, object?>()));
            return !geometry.IsEmpty;
        }

        IList<XYZ>? boundaryRect = opening.BoundaryRect;
        if (boundaryRect == null || boundaryRect.Count == 0)
        {
            return false;
        }

        if (!TryCreatePolygonFromOpeningRect(boundaryRect, projector, out Polygon2D polygonFromRect))
        {
            return false;
        }

        geometry = ToMultiPolygonGeometry(new ExportPolygon(new[] { polygonFromRect }, new Dictionary<string, object?>()));
        return !geometry.IsEmpty;
    }

    private static bool TryExtractOpeningCoverageGeometryFromSketch(
        Opening opening,
        SharedCoordinateProjector projector,
        out Geometry? geometry)
    {
        geometry = null;
        if (opening.SketchId == ElementId.InvalidElementId)
        {
            return false;
        }

        if (opening.Document.GetElement(opening.SketchId) is not Sketch sketch)
        {
            return false;
        }

        List<Geometry> geometries = new();
        foreach (CurveArray curveArray in sketch.Profile)
        {
            if (TryCreatePolygonFromCurveArray(curveArray, projector, out Polygon2D polygon))
            {
                Geometry polygonGeometry = ToMultiPolygonGeometry(
                    new ExportPolygon(new[] { polygon }, new Dictionary<string, object?>()));
                if (!polygonGeometry.IsEmpty)
                {
                    geometries.Add(polygonGeometry);
                }
            }
        }

        if (geometries.Count == 0)
        {
            return false;
        }

        Geometry unioned = SafeUnion(geometries, new List<string>());
        if (unioned.IsEmpty)
        {
            return false;
        }

        geometry = unioned;
        return true;
    }

    private static bool TryCreatePolygonFromCurveArray(
        CurveArray boundaryCurves,
        SharedCoordinateProjector projector,
        out Polygon2D polygon)
    {
        polygon = null!;
        List<Point2D> points = new();
        foreach (Curve curve in boundaryCurves)
        {
            IList<XYZ> tessellated = curve.Tessellate();
            if (tessellated.Count == 0)
            {
                tessellated = new[] { curve.GetEndPoint(0), curve.GetEndPoint(1) };
            }

            for (int i = 0; i < tessellated.Count; i++)
            {
                if (points.Count > 0 && i == 0)
                {
                    continue;
                }

                Point2D projected = projector.ProjectPoint(tessellated[i]);
                if (points.Count == 0 || !IsSamePoint(points[points.Count - 1], projected))
                {
                    points.Add(projected);
                }
            }
        }

        return TryCreatePolygonFromPoints(points, out polygon);
    }

    private static bool TryCreatePolygonFromOpeningRect(
        IList<XYZ> boundaryRect,
        SharedCoordinateProjector projector,
        out Polygon2D polygon)
    {
        polygon = null!;
        List<Point2D> points = boundaryRect
            .Select(projector.ProjectPoint)
            .ToList();
        if (TryCreatePolygonFromPoints(points, out polygon))
        {
            return true;
        }

        if (boundaryRect.Count < 2)
        {
            return false;
        }

        Point2D first = projector.ProjectPoint(boundaryRect[0]);
        Point2D second = projector.ProjectPoint(boundaryRect[1]);
        List<Point2D> rectangle = new()
        {
            new(first.X, first.Y),
            new(second.X, first.Y),
            new(second.X, second.Y),
            new(first.X, second.Y),
        };

        return TryCreatePolygonFromPoints(rectangle, out polygon);
    }

    private static bool TryCreatePolygonFromPoints(
        IReadOnlyList<Point2D> rawPoints,
        out Polygon2D polygon)
    {
        polygon = null!;
        if (rawPoints == null || rawPoints.Count < 3)
        {
            return false;
        }

        List<Point2D> points = new(rawPoints.Count + 1);
        for (int i = 0; i < rawPoints.Count; i++)
        {
            Point2D point = rawPoints[i];
            if (points.Count == 0 || !IsSamePoint(points[points.Count - 1], point))
            {
                points.Add(point);
            }
        }

        if (points.Count < 3)
        {
            return false;
        }

        if (!IsSamePoint(points[0], points[points.Count - 1]))
        {
            points.Add(points[0]);
        }

        if (points.Count < 4)
        {
            return false;
        }

        polygon = new Polygon2D(points);
        return true;
    }

    private static bool IsSamePoint(Point2D a, Point2D b)
    {
        const double tolerance = 1e-6d;
        return Math.Abs(a.X - b.X) <= tolerance &&
               Math.Abs(a.Y - b.Y) <= tolerance;
    }

    private static ExportPolygon RemapToFixtureAttributes(ExportPolygon unitFeature)
    {
        Dictionary<string, object?> attributes = new()
        {
            ["id"] = unitFeature.Attributes.TryGetValue("id", out object? id) ? id : null,
            ["type"] = unitFeature.Attributes.TryGetValue("category", out object? category) ? category : null,
            ["name"] = unitFeature.Attributes.TryGetValue("name", out object? name) ? name : null,
            ["alt_name"] = unitFeature.Attributes.TryGetValue("alt_name", out object? altName) ? altName : null,
            ["level_id"] = unitFeature.Attributes.TryGetValue("level_id", out object? levelId) ? levelId : null,
            ["source"] = unitFeature.Attributes.TryGetValue("source", out object? source) ? source : null,
            ["display_point"] = unitFeature.Attributes.TryGetValue("display_point", out object? dp) ? dp : null,
        };

        return new ExportPolygon(unitFeature.Polygons, attributes);
    }

    private const double ShaftOpeningClipMinimumFloorCoverageRatio = 0.90d;

    private static VerticalCirculationVisibilityResult? TryApplyShaftOpeningClip(
        string elementLabel,
        long elementId,
        VerticalCirculationVisibilityResult visibility,
        Geometry? floorCoverageMask,
        Geometry? openingMask,
        ICollection<string> warnings)
    {
        if (floorCoverageMask == null || floorCoverageMask.IsEmpty ||
            openingMask == null || openingMask.IsEmpty)
        {
            return null;
        }

        Geometry source = visibility.Geometry;
        double sourceArea = source.Area;
        if (sourceArea <= MinimumVerticalCirculationAreaSquareMeters)
        {
            return null;
        }

        Geometry floorIntersection = SafeOverlay(
            source,
            floorCoverageMask,
            (a, b) => a.Intersection(b).Buffer(0d),
            warnings);
        if (floorIntersection.IsEmpty)
        {
            return null;
        }

        double coverageRatio = floorIntersection.Area / sourceArea;
        if (coverageRatio < ShaftOpeningClipMinimumFloorCoverageRatio)
        {
            return null;
        }

        Geometry openingIntersection = SafeOverlay(
            source,
            openingMask,
            (a, b) => a.Intersection(b).Buffer(0d),
            warnings);
        if (openingIntersection.IsEmpty ||
            openingIntersection.Area < MinimumVerticalCirculationAreaSquareMeters)
        {
            return null;
        }

        List<Polygon2D> polygons = ExtractPolygons(openingIntersection, 0d, new GeometryRepairResult(), 0d);
        if (polygons.Count == 0)
        {
            return null;
        }

        string warning =
            $"{elementLabel} {elementId} shaft-opening clip applied " +
            $"(floor coverage {coverageRatio:P0}, clipped area {openingIntersection.Area:0.00} m\u00B2).";
        warnings.Add(warning);

        return visibility.WithMaskApplied(
            polygons,
            openingIntersection,
            openingIntersection.Area,
            visibility.CoveredEvidenceCount,
            visibility.EvidenceCoverageRatio,
            visibility.OverCoverageArea,
            warning);
    }

    private static VerticalCirculationVisibilityResult ApplyHostVerticalCirculationOcclusionMask(
        string elementLabel,
        long elementId,
        VerticalCirculationVisibilityResult visibility,
        Geometry? verticalCirculationOcclusionMask,
        ICollection<string> warnings)
    {
        if (verticalCirculationOcclusionMask == null || verticalCirculationOcclusionMask.IsEmpty)
        {
            return visibility;
        }

        Geometry clipped = SafeOverlay(
            visibility.Geometry,
            verticalCirculationOcclusionMask,
            (a, b) => a.Difference(b).Buffer(0d),
            warnings);
        if (clipped.IsEmpty)
        {
            return HandleCollapsedHostVerticalCirculationOcclusion(elementLabel, elementId, visibility, warnings);
        }

        List<Polygon2D> polygons = ExtractPolygons(clipped, 0d, new GeometryRepairResult(), 0d);
        if (polygons.Count == 0 || clipped.Area < MinimumVerticalCirculationAreaSquareMeters)
        {
            return HandleCollapsedHostVerticalCirculationOcclusion(
                elementLabel,
                elementId,
                visibility,
                warnings,
                clipped.Area < MinimumVerticalCirculationAreaSquareMeters
                    ? "collapsed below minimum area"
                    : "produced no valid polygons");
        }

        if (Math.Abs(clipped.Area - visibility.Area) <= 1e-6d)
        {
            return visibility;
        }

        return visibility.WithMaskApplied(
            polygons,
            clipped,
            clipped.Area,
            visibility.CoveredEvidenceCount,
            visibility.EvidenceCoverageRatio,
            visibility.OverCoverageArea,
            null);
    }

    private static VerticalCirculationVisibilityResult HandleCollapsedHostVerticalCirculationOcclusion(
        string elementLabel,
        long elementId,
        VerticalCirculationVisibilityResult visibility,
        ICollection<string> warnings,
        string collapseReason = "removed the clipped footprint")
    {
        string warning =
            $"{elementLabel} {elementId} floor/shaft occlusion {collapseReason}. Keeping the pre-mask result.";
        warnings.Add(warning);
        return visibility.WithWarning(warning);
    }

    private static ExportPolygon ApplyVerticalCirculationVisibilityAttributes(
        ExportPolygon feature,
        VerticalCirculationVisibilityResult visibility)
    {
        Dictionary<string, object?> attributes = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> kvp in feature.Attributes)
        {
            attributes[kvp.Key] = kvp.Value;
        }

        foreach (KeyValuePair<string, object?> kvp in visibility.BuildDebugAttributes())
        {
            attributes[kvp.Key] = kvp.Value;
        }

        return new ExportPolygon(feature.Polygons, attributes);
    }

    private void AddLinkedUnitFeatures(
        ViewExportContext context,
        string levelId,
        IExportMetadataProvider metadataProvider,
        FloorCategoryResolver floorCategoryResolver,
        RoomCategoryResolver roomCategoryResolver,
        IReadOnlyDictionary<string, string> familyCategoryOverrides,
        string roomCategoryParameterName,
        SchemaProfile activeSchemaProfile,
        ExportLayer? floorUnitLayer,
        ExportLayer? roomUnitLayer,
        ExportLayer? supplementalUnitLayer,
        ExportLayer? fixtureLayer,
        ICollection<string> warnings,
        bool simplifyStairUnits,
        bool simplifyEscalatorUnits)
    {
        if (context.LinkedSources.Count == 0)
        {
            return;
        }

        foreach (LinkedViewSourceContext linkedSource in context.LinkedSources)
        {
            ExportSourceDescriptor sourceDescriptor = ExportSourceDescriptor.CreateLinked(_document, linkedSource);
            UnitExtractor linkedUnitExtractor = new(
                linkedSource.LinkedDocument,
                _zoneCatalog,
                metadataProvider,
                linkedSource.SourceDocumentName,
                floorCategoryResolver,
                roomCategoryResolver,
                familyCategoryOverrides,
                sourceDescriptor,
                activeSchemaProfile,
                simplifyEscalatorUnits: simplifyEscalatorUnits);

            if (roomUnitLayer != null)
            {
                AddRoomUnits(
                    levelId,
                    linkedSource.Rooms,
                    linkedUnitExtractor,
                    roomUnitLayer,
                    roomCategoryParameterName,
                    context.View.Name,
                    warnings);
            }

            if (floorUnitLayer != null)
            {
                AddFloorUnits(levelId, linkedSource.Floors, linkedUnitExtractor, floorUnitLayer, context.View.Name, warnings);
            }

            if (RawFloorOnlyDebugMode || supplementalUnitLayer == null)
            {
                continue;
            }

            Geometry? linkedFloorCoverageMask = BuildFloorCoverageMask(
                levelId,
                linkedSource.Floors,
                linkedUnitExtractor,
                context.View.Name,
                warnings);
            Dictionary<long, ExportPolygon> linkedStairVisibilityFeatures = BuildStairVisibilityFeatures(
                levelId,
                context.View,
                linkedSource.Stairs,
                linkedUnitExtractor,
                linkedFloorCoverageMask,
                warnings,
                simplifyStairUnits);
            Dictionary<long, ExportPolygon> linkedEscalatorVisibilityFeatures = BuildEscalatorVisibilityFeatures(
                levelId,
                context.View,
                linkedSource.FamilyUnits,
                linkedUnitExtractor,
                warnings,
                simplifyEscalatorUnits,
                linkedSource.TransformToHost.Origin.Z);
            AddVerticalCirculationUnits(
                linkedStairVisibilityFeatures.Values.Concat(linkedEscalatorVisibilityFeatures.Values),
                supplementalUnitLayer);
            HashSet<long> precomputedLinkedEscalatorIds = new(linkedEscalatorVisibilityFeatures.Keys);
            AddFamilyUnits(
                levelId,
                context.View,
                linkedSource.FamilyUnits,
                linkedUnitExtractor,
                supplementalUnitLayer,
                fixtureLayer,
                warnings,
                precomputedLinkedEscalatorIds);
        }
    }

    private void AddLinkedDetailFeatures(
        ViewExportContext context,
        string levelId,
        GeometryRepairOptions geometryRepairOptions,
        GeometryRepairResult geometryRepair,
        ExportLayer detailLayer,
        SchemaProfile activeSchemaProfile,
        ICollection<string> warnings)
    {
        if (context.LinkedSources.Count == 0)
        {
            return;
        }

        foreach (LinkedViewSourceContext linkedSource in context.LinkedSources)
        {
            DetailExtractor linkedDetailExtractor = new(
                linkedSource.LinkedDocument,
                geometryRepairOptions,
                ExportSourceDescriptor.CreateLinked(_document, linkedSource),
                activeSchemaProfile);
            foreach (ExportLineString detailFeature in linkedDetailExtractor.ExtractForLevel(
                         context.Level,
                         levelId,
                         linkedSource.DetailCurves,
                         linkedSource.Stairs,
                         geometryRepair,
                         warnings,
                         view: null,
                         viewName: context.View.Name,
                         stairVisibilityResults: null,
                         skipLevelFilter: true))
            {
                detailLayer.AddFeature(detailFeature);
            }
        }
    }

    private void AddLinkedOpeningFeatures(
        ViewExportContext context,
        string levelId,
        IExportMetadataProvider metadataProvider,
        GeometryRepairOptions geometryRepairOptions,
        IReadOnlyList<ExportPolygon> unitFeatures,
        GeometryRepairResult geometryRepair,
        ExportLayer openingLayer,
        SchemaProfile activeSchemaProfile,
        ICollection<string> warnings)
    {
        if (context.LinkedSources.Count == 0)
        {
            return;
        }

        foreach (LinkedViewSourceContext linkedSource in context.LinkedSources)
        {
            OpeningExtractor linkedOpeningExtractor = new(
                linkedSource.LinkedDocument,
                metadataProvider,
                _zoneCatalog,
                geometryRepairOptions,
                ExportSourceDescriptor.CreateLinked(_document, linkedSource),
                activeSchemaProfile);
            foreach (ExportLineString openingFeature in linkedOpeningExtractor.ExtractForLevel(
                         context.Level,
                         levelId,
                         linkedSource.Openings,
                         unitFeatures,
                         geometryRepair,
                         warnings,
                         context.View.Name,
                         skipLevelFilter: true))
            {
                openingLayer.AddFeature(openingFeature);
            }
        }
    }

    private static List<ExportPolygon> NormalizeUnitFeatures(
        IReadOnlyList<ExportPolygon> unitFeatures,
        GeometryRepairOptions geometryRepairOptions,
        GeometryRepairResult geometryRepair,
        ICollection<string> warnings)
    {
        if (unitFeatures.Count == 0)
        {
            return new List<ExportPolygon>();
        }

        List<UnitGeometryRecord> converted = new(unitFeatures.Count);
        List<Geometry> verticalGeometries = new();

        for (int i = 0; i < unitFeatures.Count; i++)
        {
            ExportPolygon feature = unitFeatures[i];
            Geometry geometry = ToMultiPolygonGeometry(feature);
            if (geometry.IsEmpty)
            {
                continue;
            }

            if (geometryRepairOptions.Enabled && geometryRepairOptions.SimplifyToleranceMeters > 0d)
            {
                Geometry simplified = TopologyPreservingSimplifier.Simplify(geometry, geometryRepairOptions.SimplifyToleranceMeters);
                if (!simplified.IsEmpty)
                {
                    geometry = simplified;
                    geometryRepair.SimplifiedPolygons++;
                }
            }

            string category = GetCategory(feature);
            UnitGeometryRecord record = new(feature.Attributes, category, geometry);
            converted.Add(record);
            if (IsVerticalFillCategory(category))
            {
                verticalGeometries.Add(geometry);
            }
        }

        if (converted.Count == 0)
        {
            return new List<ExportPolygon>();
        }

        Geometry globalVertical = Geometry.DefaultFactory.CreateGeometryCollection(Array.Empty<Geometry>());
        if (verticalGeometries.Count > 0)
        {
            try
            {
                globalVertical = UnaryUnionOp.Union(verticalGeometries).Buffer(0d);
            }
            catch (TopologyException)
            {
                try
                {
                    GeometryPrecisionReducer reducer = new(new PrecisionModel(100_000d));
                    List<Geometry> reduced = verticalGeometries.Select(g => reducer.Reduce(g)).ToList();
                    globalVertical = UnaryUnionOp.Union(reduced).Buffer(0d);
                    warnings.Add("Global vertical unit union required reduced precision.");
                }
                catch (TopologyException)
                {
                    warnings.Add("Global vertical unit union failed.");
                }
            }
        }

        for (int i = 0; i < converted.Count; i++)
        {
            UnitGeometryRecord record = converted[i];
            if (!IsVerticalFillCategory(record.Category) && !globalVertical.IsEmpty)
            {
                Geometry removed = SafeOverlay(
                    record.Geometry,
                    globalVertical,
                    (a, b) => a.Intersection(b).Buffer(0d),
                    warnings);
                Geometry trimmed = SafeOverlay(
                    record.Geometry,
                    globalVertical,
                    (a, b) => a.Difference(b).Buffer(0d),
                    warnings);
                converted[i] = new UnitGeometryRecord(record.Attributes, record.Category, trimmed, removed);
            }
        }

        if (geometryRepairOptions.Enabled)
        {
            CloseSmallGaps(converted, geometryRepairOptions.MergeNearbyBoundaryThresholdMeters);
        }

        List<ExportPolygon> exported = BuildExportPolygons(
            converted,
            geometryRepairOptions.MinimumPolygonAreaSquareMeters,
            geometryRepairOptions.MaxHoleSizeMeters,
            geometryRepair);
        ApplyVerticalDifferenceConsistencyCheck(
            converted,
            exported,
            geometryRepairOptions.MinimumPolygonAreaSquareMeters,
            geometryRepairOptions.MaxHoleSizeMeters,
            geometryRepair,
            warnings);

        return exported;
    }

    private static void ApplyVerticalDifferenceConsistencyCheck(
        List<UnitGeometryRecord> records,
        List<ExportPolygon> exported,
        double minimumPolygonAreaSquareMeters,
        double maxHoleSizeMeters,
        GeometryRepairResult geometryRepair,
        ICollection<string> warnings)
    {
        List<Geometry> removedByVertical = records
            .Where(r => !IsVerticalFillCategory(r.Category) && r.RemovedByVertical != null && !r.RemovedByVertical.IsEmpty)
            .Select(r => r.RemovedByVertical!)
            .ToList();
        if (removedByVertical.Count == 0)
        {
            return;
        }

        Geometry removedUnion = SafeUnion(removedByVertical, warnings);
        if (removedUnion.IsEmpty)
        {
            return;
        }

        List<Geometry> survivingVertical = exported
            .Where(f => IsVerticalFillCategory(GetCategory(f)))
            .Select(ToMultiPolygonGeometry)
            .Where(g => !g.IsEmpty)
            .ToList();
        Geometry survivingVerticalUnion = survivingVertical.Count == 0
            ? GeometryFactory.CreateGeometryCollection(Array.Empty<Geometry>())
            : SafeUnion(survivingVertical, warnings);

        Geometry orphanRemoved = survivingVerticalUnion.IsEmpty
            ? removedUnion
            : SafeOverlay(removedUnion, survivingVerticalUnion, (a, b) => a.Difference(b).Buffer(0d), warnings);
        if (orphanRemoved.IsEmpty || orphanRemoved.Area <= 1e-8d)
        {
            return;
        }

        Dictionary<string, int> sourceCategoryCounts = CountCategories(records.Select(r => r.Category));
        Dictionary<string, int> exportedCategoryCounts = CountCategories(exported.Select(GetCategory));
        warnings.Add(
            $"Vertical subtraction mismatch detected (source features: {FormatCategoryCounts(sourceCategoryCounts)}; " +
            $"surviving features: {FormatCategoryCounts(exportedCategoryCounts)}). " +
            "Restoring dropped vertical void regions to keep boundaries consistent.");

        for (int i = 0; i < records.Count; i++)
        {
            UnitGeometryRecord record = records[i];
            if (IsVerticalFillCategory(record.Category) || record.RemovedByVertical == null || record.RemovedByVertical.IsEmpty)
            {
                continue;
            }

            Geometry restore = SafeOverlay(record.RemovedByVertical, orphanRemoved, (a, b) => a.Intersection(b).Buffer(0d), warnings);
            if (restore.IsEmpty)
            {
                continue;
            }

            Geometry restoredGeometry = SafeOverlay(record.Geometry, restore, (a, b) => a.Union(b).Buffer(0d), warnings);
            records[i] = new UnitGeometryRecord(record.Attributes, record.Category, restoredGeometry);
        }

        exported.Clear();
        exported.AddRange(BuildExportPolygons(records, minimumPolygonAreaSquareMeters, maxHoleSizeMeters, geometryRepair));
    }

    private static List<ExportPolygon> BuildExportPolygons(
        IReadOnlyList<UnitGeometryRecord> records,
        double minimumPolygonAreaSquareMeters,
        double maxHoleSizeMeters,
        GeometryRepairResult geometryRepair)
    {
        List<ExportPolygon> exported = new(records.Count);
        for (int i = 0; i < records.Count; i++)
        {
            ExportPolygon? feature = ToExportPolygon(records[i].Geometry, records[i].Attributes, minimumPolygonAreaSquareMeters, maxHoleSizeMeters, geometryRepair);
            if (feature != null)
            {
                exported.Add(feature);
            }
        }

        return exported;
    }

    private static void CloseSmallGaps(List<UnitGeometryRecord> records, double gapThresholdMeters)
    {
        if (gapThresholdMeters <= 0d)
        {
            return;
        }

        double halfGap = gapThresholdMeters / 2d;
        List<Geometry> originals = records.Select(r => r.Geometry).ToList();
        STRtree<int> originalIndex = new();
        for (int i = 0; i < originals.Count; i++)
        {
            Envelope envelope = originals[i].EnvelopeInternal;
            if (envelope != null && !envelope.IsNull)
            {
                originalIndex.Insert(envelope, i);
            }
        }

        for (int i = 0; i < records.Count; i++)
        {
            if (IsVerticalFillCategory(records[i].Category))
            {
                continue;
            }

            Geometry buffered = records[i].Geometry.Buffer(halfGap);
            Envelope bufferedEnvelope = buffered.EnvelopeInternal;
            if (bufferedEnvelope == null || bufferedEnvelope.IsNull)
            {
                continue;
            }

            foreach (int j in originalIndex.Query(bufferedEnvelope))
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
                records[i] = new UnitGeometryRecord(current.Attributes, current.Category, buffered, current.RemovedByVertical);
            }
        }
    }

    private static Dictionary<string, int> CountCategories(IEnumerable<string> categories)
    {
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        foreach (string category in categories)
        {
            string key = string.IsNullOrWhiteSpace(category) ? "(uncategorized)" : category.Trim();
            counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
        }

        return counts;
    }

    private static string FormatCategoryCounts(IReadOnlyDictionary<string, int> counts)
    {
        return string.Join(", ", counts
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => $"{kvp.Key}={kvp.Value}"));
    }

    private static Geometry SafeUnion(IReadOnlyList<Geometry> geometries, ICollection<string> warnings)
    {
        try
        {
            return UnaryUnionOp.Union(geometries).Buffer(0d);
        }
        catch (TopologyException)
        {
            try
            {
                GeometryPrecisionReducer reducer = new(new PrecisionModel(100_000d));
                List<Geometry> reduced = geometries.Select(g => reducer.Reduce(g)).ToList();
                warnings.Add("A geometry union required reduced precision and may be slightly approximated.");
                return UnaryUnionOp.Union(reduced).Buffer(0d);
            }
            catch (TopologyException)
            {
                warnings.Add("A geometry union failed; keeping original geometry set.");
                return GeometryFactory.CreateGeometryCollection(Array.Empty<Geometry>());
            }
        }
    }

    private static ExportPolygon? ToExportPolygon(
        Geometry geometry,
        IReadOnlyDictionary<string, object?> attributes,
        double minimumPolygonAreaSquareMeters,
        double maxHoleSizeMeters,
        GeometryRepairResult geometryRepair)
    {
        if (geometry == null || geometry.IsEmpty)
        {
            return null;
        }

        List<Polygon2D> polygons = ExtractPolygons(geometry, minimumPolygonAreaSquareMeters, geometryRepair, maxHoleSizeMeters);
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

    private static Geometry? UnionPolygonsToGeometry(
        IReadOnlyList<Polygon2D> polygons,
        ICollection<string> warnings)
    {
        if (polygons == null || polygons.Count == 0)
        {
            return null;
        }

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

        try
        {
            return geoms.Count == 1 ? geoms[0] : UnaryUnionOp.Union(geoms).Buffer(0d);
        }
        catch (TopologyException ex)
        {
            warnings.Add($"Polygon union failed: {ex.Message}");
            return null;
        }
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

    private static List<Polygon2D> ExtractPolygons(
        Geometry geometry,
        double minimumPolygonAreaSquareMeters,
        GeometryRepairResult geometryRepair,
        double maxHoleSizeMeters)
    {
        List<Polygon2D> polygons = new();
        switch (geometry)
        {
            case Polygon polygon:
                AddPolygonIfValid(polygons, polygon, minimumPolygonAreaSquareMeters, geometryRepair, maxHoleSizeMeters);
                break;
            case MultiPolygon multiPolygon:
                for (int i = 0; i < multiPolygon.NumGeometries; i++)
                {
                    if (multiPolygon.GetGeometryN(i) is Polygon child)
                    {
                        AddPolygonIfValid(polygons, child, minimumPolygonAreaSquareMeters, geometryRepair, maxHoleSizeMeters);
                    }
                }

                break;
            case GeometryCollection collection:
                for (int i = 0; i < collection.NumGeometries; i++)
                {
                    polygons.AddRange(ExtractPolygons(collection.GetGeometryN(i), minimumPolygonAreaSquareMeters, geometryRepair, maxHoleSizeMeters));
                }

                break;
        }

        return polygons;
    }

    private static void AddPolygonIfValid(
        ICollection<Polygon2D> target,
        Polygon polygon,
        double minimumPolygonAreaSquareMeters,
        GeometryRepairResult geometryRepair,
        double maxHoleSizeMeters)
    {
        if (polygon.IsEmpty || polygon.Area < minimumPolygonAreaSquareMeters)
        {
            geometryRepair.DroppedPolygons++;
            return;
        }

        Polygon2D? converted = ToPolygon2D(polygon, maxHoleSizeMeters);
        if (converted != null)
        {
            target.Add(converted);
        }
    }

    private static Polygon2D? ToPolygon2D(Polygon polygon, double maxHoleSizeMeters)
    {
        IReadOnlyList<Point2D>? exterior = ToPointList(polygon.ExteriorRing.Coordinates);
        if (exterior == null)
        {
            return null;
        }

        List<IReadOnlyList<Point2D>> interior = new();
        for (int i = 0; i < polygon.NumInteriorRings; i++)
        {
            LineString holeRing = polygon.GetInteriorRingN(i);
            if (maxHoleSizeMeters > 0d && IsSmallHole(holeRing, maxHoleSizeMeters))
            {
                continue;
            }

            IReadOnlyList<Point2D>? ring = ToPointList(holeRing.Coordinates);
            if (ring != null)
            {
                interior.Add(ring);
            }
        }

        return new Polygon2D(exterior, interior);
    }

    private static bool IsSmallHole(LineString ring, double maxHoleSizeMeters)
    {
        Envelope envelope = ring.EnvelopeInternal;
        if (envelope == null || envelope.IsNull)
        {
            return false;
        }

        return Math.Max(envelope.Width, envelope.Height) <= maxHoleSizeMeters;
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

    private static IReadOnlyDictionary<string, string> EmptyOverrides()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal);
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

    private readonly struct HostStairOcclusionContext
    {
        public HostStairOcclusionContext(
            Geometry? floorCoverageMask,
            Geometry? openingMask,
            Geometry? occlusionMask)
        {
            FloorCoverageMask = floorCoverageMask;
            OpeningMask = openingMask;
            OcclusionMask = occlusionMask;
        }

        public Geometry? FloorCoverageMask { get; }

        public Geometry? OpeningMask { get; }

        public Geometry? OcclusionMask { get; }
    }

    private readonly struct UnitGeometryRecord
    {
        public UnitGeometryRecord(
            IReadOnlyDictionary<string, object?> attributes,
            string category,
            Geometry geometry,
            Geometry? removedByVertical = null)
        {
            Attributes = attributes;
            Category = category;
            Geometry = geometry;
            RemovedByVertical = removedByVertical;
        }

        public IReadOnlyDictionary<string, object?> Attributes { get; }

        public string Category { get; }

        public Geometry Geometry { get; }

        public Geometry? RemovedByVertical { get; }
    }
}
