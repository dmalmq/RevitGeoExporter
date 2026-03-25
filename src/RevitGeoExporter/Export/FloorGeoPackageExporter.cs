using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Diagnostics;
using RevitGeoExporter.Core.GeoPackage;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Schema;
using RevitGeoExporter.Core.Shapefile;
using RevitGeoExporter.Core.Validation;
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
        IncrementalExportMode incrementalExportMode = IncrementalExportMode.AllSelectedViews,
        CoordinateExportMode coordinateMode = CoordinateExportMode.SharedCoordinates,
        int? sourceEpsg = null,
        string? sourceCoordinateSystemId = null,
        string? sourceCoordinateSystemDefinition = null,
        UnitSource unitSource = UnitSource.Floors,
        UnitGeometrySource unitGeometrySource = UnitGeometrySource.Unset,
        UnitAttributeSource unitAttributeSource = UnitAttributeSource.Unset,
        string roomCategoryParameterName = "Name",
        LinkExportOptions? linkExportOptions = null,
        SchemaProfile? activeSchemaProfile = null,
        ValidationPolicyProfile? activeValidationPolicyProfile = null,
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
            incrementalExportMode,
            coordinateMode,
            sourceEpsg,
            sourceCoordinateSystemId,
            sourceCoordinateSystemDefinition,
            unitSource,
            unitGeometrySource,
            unitAttributeSource,
            roomCategoryParameterName,
            linkExportOptions,
            activeSchemaProfile,
            activeValidationPolicyProfile);
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
        IncrementalExportMode incrementalExportMode = IncrementalExportMode.AllSelectedViews,
        CoordinateExportMode coordinateMode = CoordinateExportMode.SharedCoordinates,
        int? sourceEpsg = null,
        string? sourceCoordinateSystemId = null,
        string? sourceCoordinateSystemDefinition = null,
        UnitSource unitSource = UnitSource.Floors,
        UnitGeometrySource unitGeometrySource = UnitGeometrySource.Unset,
        UnitAttributeSource unitAttributeSource = UnitAttributeSource.Unset,
        string roomCategoryParameterName = "Name",
        LinkExportOptions? linkExportOptions = null,
        SchemaProfile? activeSchemaProfile = null,
        ValidationPolicyProfile? activeValidationPolicyProfile = null)
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
        ValidationPolicyProfile effectiveValidationPolicyProfile = activeValidationPolicyProfile?.Clone() ?? ValidationPolicyProfile.CreateRecommendedProfile();

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
                UnitGeometrySource = unitGeometrySource,
                UnitAttributeSource = unitAttributeSource,
                RoomCategoryParameterName = roomCategoryParameterName,
                LinkExportOptions = effectiveLinkExportOptions,
                ActiveSchemaProfile = effectiveSchemaProfile,
                ActiveValidationPolicyProfile = effectiveValidationPolicyProfile,
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
            incrementalExportMode,
            overrideLoad.Value,
            roomOverrideLoad.Value,
            familyOverrideLoad.Value,
            acceptedOpeningLoad.Value,
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
            unitGeometrySource,
            unitAttributeSource,
            roomCategoryParameterName,
            effectiveLinkExportOptions,
            effectiveSchemaProfile,
            effectiveValidationPolicyProfile,
            BuildIncludedLinks(contexts));
    }

    public FloorGeoPackageExportResult WritePreparedExport(
        PreparedExportSession session,
        Action<ExportProgressUpdate>? progressCallback = null,
        CancellationToken cancellationToken = default)
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

        ExportBaselineLoadResult baseline = new ExportBaselineStore().Load(session.BaselineKey);
        Dictionary<long, ViewChangeDecision> viewDecisions = BuildViewChangeDecisions(session, baseline.Snapshot);
        List<ArtifactPlan> artifactPlans = BuildArtifactPlans(session);
        ExportExecutionSummary executionSummary = BuildExecutionSummary(session, baseline.Snapshot, viewDecisions);
        int missingBaselineArtifactCount = 0;

        foreach (ArtifactPlan plan in artifactPlans)
        {
            bool hasChangedView = plan.ContributingViewIds.Any(viewId => viewDecisions.TryGetValue(viewId, out ViewChangeDecision? decision) && decision.HasChanges);
            bool canReuse = session.IncrementalExportMode == IncrementalExportMode.ChangedViewsOnly &&
                            executionSummary.FullRewriteReason == null &&
                            !hasChangedView &&
                            CanReuseArtifact(baseline.Snapshot, plan);
            if (!canReuse &&
                session.IncrementalExportMode == IncrementalExportMode.ChangedViewsOnly &&
                executionSummary.FullRewriteReason == null &&
                !hasChangedView)
            {
                missingBaselineArtifactCount++;
            }

            plan.ShouldWrite = !canReuse;
        }

        executionSummary.MissingBaselineArtifactCount = missingBaselineArtifactCount;
        result.SetExecutionSummary(executionSummary);

        int totalSteps = Math.Max(1, artifactPlans.Count(plan => plan.ShouldWrite));
        int completedSteps = 0;
        progressCallback?.Invoke(new ExportProgressUpdate(0, totalSteps, "Preparing export..."));

        GpkgWriter gpkgWriter = session.OutputFormat == ExportFormat.GeoPackage ? new GpkgWriter() : null;
        ShapefileWriter shpWriter = session.OutputFormat == ExportFormat.Shapefile ? new ShapefileWriter() : null;

        foreach (ArtifactPlan plan in artifactPlans)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!plan.ShouldWrite)
            {
                result.AddArtifactResult(plan.ToResult(ArtifactDisposition.ReusedFromBaseline));
                continue;
            }

            progressCallback?.Invoke(new ExportProgressUpdate(completedSteps, totalSteps, $"Writing {plan.ArtifactName}..."));

            ExportLayer[] layers = plan.Layers
                .Select(layerPlan => PrepareLayerForWrite(layerPlan.Layer, shouldTransform, sourceCoordinateSystem, targetCoordinateSystem))
                .ToArray();

            if (shpWriter != null)
            {
                shpWriter.Write(plan.OutputFilePath, session.OutputEpsg, layers);
            }
            else
            {
                gpkgWriter!.Write(plan.OutputFilePath, session.OutputEpsg, layers);
            }

            result.AddArtifactResult(plan.ToResult(ArtifactDisposition.Written));
            completedSteps++;
            progressCallback?.Invoke(new ExportProgressUpdate(completedSteps, totalSteps, $"Exported {plan.ArtifactName}"));
        }

        result.SetPendingBaselineSnapshot(BuildBaselineSnapshot(session, viewDecisions, artifactPlans));
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

    private static Dictionary<long, ViewChangeDecision> BuildViewChangeDecisions(
        PreparedExportSession session,
        ExportBaselineSnapshot? baselineSnapshot)
    {
        ExportFingerprintBuilder fingerprintBuilder = new();
        string configurationFingerprint = BuildConfigurationFingerprint(session, fingerprintBuilder);
        Dictionary<long, ExportBaselineViewSnapshot> baselineViews = baselineSnapshot?.Views
            .GroupBy(view => view.ViewId)
            .Select(group => group.First())
            .ToDictionary(view => view.ViewId) ?? new Dictionary<long, ExportBaselineViewSnapshot>();
        Dictionary<long, ViewChangeDecision> decisions = new();

        bool forceRewrite = session.IncrementalExportMode == IncrementalExportMode.AllSelectedViews ||
                            baselineSnapshot == null ||
                            !string.Equals(
                                baselineSnapshot.ConfigurationFingerprint,
                                configurationFingerprint,
                                StringComparison.Ordinal);

        foreach (PreparedViewExportData viewData in session.Prepared.Views)
        {
            string fingerprint = fingerprintBuilder.ComputeLayerFingerprint(GetSelectedLayers(session.FeatureTypes, viewData));
            bool hasChanges;
            string? reason = null;
            if (forceRewrite)
            {
                hasChanges = true;
            }
            else if (!baselineViews.TryGetValue(viewData.View.Id.Value, out ExportBaselineViewSnapshot? baselineView))
            {
                hasChanges = true;
                reason = "The view did not exist in the previous baseline.";
            }
            else
            {
                hasChanges = !string.Equals(baselineView.ContentFingerprint, fingerprint, StringComparison.Ordinal);
            }

            decisions[viewData.View.Id.Value] = new ViewChangeDecision(viewData, fingerprint, hasChanges, reason);
        }

        return decisions;
    }

    private static ExportExecutionSummary BuildExecutionSummary(
        PreparedExportSession session,
        ExportBaselineSnapshot? baselineSnapshot,
        IReadOnlyDictionary<long, ViewChangeDecision> viewDecisions)
    {
        string? fullRewriteReason = null;
        if (session.IncrementalExportMode == IncrementalExportMode.AllSelectedViews)
        {
            fullRewriteReason = "Incremental export mode is set to export all selected views.";
        }
        else if (baselineSnapshot == null)
        {
            fullRewriteReason = "No previous export baseline snapshot was found.";
        }
        else
        {
            ExportFingerprintBuilder fingerprintBuilder = new();
            string currentConfigurationFingerprint = BuildConfigurationFingerprint(session, fingerprintBuilder);
            if (!string.Equals(baselineSnapshot.ConfigurationFingerprint, currentConfigurationFingerprint, StringComparison.Ordinal))
            {
                fullRewriteReason = "Export-affecting settings changed since the previous baseline.";
            }
        }

        return new ExportExecutionSummary
        {
            IncrementalExportMode = session.IncrementalExportMode,
            ChangedViewCount = viewDecisions.Values.Count(decision => decision.HasChanges),
            ReusedViewCount = viewDecisions.Values.Count(decision => !decision.HasChanges),
            FullRewriteReason = fullRewriteReason,
        };
    }

    private static string BuildConfigurationFingerprint(PreparedExportSession session, ExportFingerprintBuilder fingerprintBuilder)
    {
        List<string> inputs = new()
        {
            $"featureTypes:{session.FeatureTypes}",
            $"coordinateMode:{session.CoordinateMode}",
            $"targetEpsg:{session.TargetEpsg}",
            $"outputEpsg:{session.OutputEpsg}",
            $"sourceEpsg:{session.SourceEpsg?.ToString() ?? "<none>"}",
            $"unitSource:{session.UnitSource}",
            $"unitGeometrySource:{session.UnitGeometrySource}",
            $"unitAttributeSource:{session.UnitAttributeSource}",
            $"roomCategoryParameter:{session.RoomCategoryParameterName}",
            $"schemaProfile:{SerializeSchemaProfile(session.ActiveSchemaProfile)}",
            $"linkOptions:{session.LinkExportOptions.IncludeLinkedModels}|{string.Join(",", session.LinkExportOptions.SelectedLinkInstanceIds.OrderBy(id => id))}",
        };

        inputs.AddRange(session.FloorCategoryOverrides.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => $"floor:{entry.Key}={entry.Value}"));
        inputs.AddRange(session.RoomCategoryOverrides.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => $"room:{entry.Key}={entry.Value}"));
        inputs.AddRange(session.FamilyCategoryOverrides.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => $"family:{entry.Key}={entry.Value}"));
        inputs.AddRange(session.AcceptedOpeningFamilies.OrderBy(value => value, StringComparer.Ordinal).Select(value => $"opening:{value}"));

        return fingerprintBuilder.ComputeConfigurationFingerprint(inputs);
    }

    private static string SerializeSchemaProfile(SchemaProfile profile)
    {
        return string.Join(
            "|",
            profile.Mappings
                .OrderBy(mapping => mapping.Layer)
                .ThenBy(mapping => mapping.FieldName, StringComparer.Ordinal)
                .ThenBy(mapping => mapping.SourceKey, StringComparer.Ordinal)
                .Select(mapping => $"{mapping.Layer}:{mapping.FieldName}:{mapping.SourceKind}:{mapping.SourceKey}:{mapping.ConstantValue}:{mapping.TargetType}:{mapping.NullBehavior}:{mapping.DefaultValue}"));
    }

    private static List<ArtifactPlan> BuildArtifactPlans(PreparedExportSession session)
    {
        string safeModelName = SanitizeFileName(session.SourceModelName);
        bool isShapefile = session.OutputFormat == ExportFormat.Shapefile;
        string ext = isShapefile ? ".shp" : ".gpkg";
        HashSet<string> usedFileStems = new(StringComparer.OrdinalIgnoreCase);
        List<ArtifactPlan> plans = new();

        switch (session.PackageOptions.PackagingMode)
        {
            case PackagingMode.PerViewPerFeatureFiles:
                foreach (PreparedViewExportData viewData in session.Prepared.Views)
                {
                    string fileStem = BuildUniqueFileStem(
                        safeModelName,
                        SanitizeFileName(viewData.View.Name),
                        viewData.View.Id.Value,
                        usedFileStems);
                    foreach ((string layerName, ExportLayer layer) in GetLayerEntries(session.FeatureTypes, viewData))
                    {
                        if (isShapefile && layer.Features.Count == 0)
                        {
                            continue;
                        }

                        string outputFilePath = Path.Combine(session.OutputDirectory, $"{fileStem}_{layerName}{ext}");
                        plans.Add(new ArtifactPlan(
                            $"view:{viewData.View.Id.Value}|layer:{layerName}",
                            Path.GetFileName(outputFilePath),
                            outputFilePath,
                            session.PackageOptions.PackagingMode,
                            new[] { viewData },
                            new[] { new ArtifactLayerPlan(layerName, layer) }));
                    }
                }

                break;

            case PackagingMode.PerViewGeoPackage:
                foreach (PreparedViewExportData viewData in session.Prepared.Views)
                {
                    List<ArtifactLayerPlan> layers = GetLayerEntries(session.FeatureTypes, viewData)
                        .Select(entry => new ArtifactLayerPlan(entry.LayerName, entry.Layer))
                        .ToList();
                    if (layers.Count == 0)
                    {
                        continue;
                    }

                    string fileStem = BuildUniqueFileStem(
                        safeModelName,
                        SanitizeFileName(viewData.View.Name),
                        viewData.View.Id.Value,
                        usedFileStems);
                    if (isShapefile)
                    {
                        foreach (ArtifactLayerPlan layer in layers.Where(layer => layer.Layer.Features.Count > 0))
                        {
                            string outputFilePath = Path.Combine(session.OutputDirectory, $"{fileStem}_{layer.LayerName}{ext}");
                            plans.Add(new ArtifactPlan(
                                $"view:{viewData.View.Id.Value}|layer:{layer.LayerName}",
                                Path.GetFileName(outputFilePath),
                                outputFilePath,
                                session.PackageOptions.PackagingMode,
                                new[] { viewData },
                                new[] { layer }));
                        }
                    }
                    else
                    {
                        string outputFilePath = Path.Combine(session.OutputDirectory, $"{fileStem}{ext}");
                        plans.Add(new ArtifactPlan(
                            $"view:{viewData.View.Id.Value}",
                            Path.GetFileName(outputFilePath),
                            outputFilePath,
                            session.PackageOptions.PackagingMode,
                            new[] { viewData },
                            layers));
                    }
                }

                break;

            case PackagingMode.PerLevelGeoPackage:
                foreach (IGrouping<long, PreparedViewExportData> group in session.Prepared.Views.GroupBy(view => view.Level.Id.Value))
                {
                    List<PreparedViewExportData> viewGroup = group.ToList();
                    if (viewGroup.Count == 0)
                    {
                        continue;
                    }

                    List<ArtifactLayerPlan> layers = MergeLayerPlans(session.FeatureTypes, viewGroup);
                    if (layers.Count == 0)
                    {
                        continue;
                    }

                    PreparedViewExportData representative = viewGroup[0];
                    string stem = BuildUniqueFileStem(
                        safeModelName,
                        $"{SanitizeFileName(representative.Level.Name)}_level",
                        representative.Level.Id.Value,
                        usedFileStems);
                    if (isShapefile)
                    {
                        foreach (ArtifactLayerPlan layer in layers.Where(layer => layer.Layer.Features.Count > 0))
                        {
                            string outputFilePath = Path.Combine(session.OutputDirectory, $"{stem}_{layer.LayerName}{ext}");
                            plans.Add(new ArtifactPlan(
                                $"level:{representative.Level.Id.Value}|layer:{layer.LayerName}",
                                Path.GetFileName(outputFilePath),
                                outputFilePath,
                                session.PackageOptions.PackagingMode,
                                viewGroup,
                                new[] { layer }));
                        }
                    }
                    else
                    {
                        string outputFilePath = Path.Combine(session.OutputDirectory, $"{stem}{ext}");
                        plans.Add(new ArtifactPlan(
                            $"level:{representative.Level.Id.Value}",
                            Path.GetFileName(outputFilePath),
                            outputFilePath,
                            session.PackageOptions.PackagingMode,
                            viewGroup,
                            layers));
                    }
                }

                break;

            case PackagingMode.PerBuildingGeoPackage:
                List<ArtifactLayerPlan> buildingLayers = MergeLayerPlans(session.FeatureTypes, session.Prepared.Views);
                if (buildingLayers.Count > 0)
                {
                    if (isShapefile)
                    {
                        foreach (ArtifactLayerPlan layer in buildingLayers.Where(layer => layer.Layer.Features.Count > 0))
                        {
                            string outputFilePath = Path.Combine(session.OutputDirectory, $"{safeModelName}_building_{layer.LayerName}{ext}");
                            plans.Add(new ArtifactPlan(
                                $"building:{session.SourceDocumentKey}|layer:{layer.LayerName}",
                                Path.GetFileName(outputFilePath),
                                outputFilePath,
                                session.PackageOptions.PackagingMode,
                                session.Prepared.Views,
                                new[] { layer }));
                        }
                    }
                    else
                    {
                        string outputFilePath = Path.Combine(session.OutputDirectory, $"{safeModelName}_building{ext}");
                        plans.Add(new ArtifactPlan(
                            $"building:{session.SourceDocumentKey}",
                            Path.GetFileName(outputFilePath),
                            outputFilePath,
                            session.PackageOptions.PackagingMode,
                            session.Prepared.Views,
                            buildingLayers));
                    }
                }

                break;
        }

        return plans;
    }

    private static List<ArtifactLayerPlan> MergeLayerPlans(
        ExportFeatureType featureTypes,
        IReadOnlyList<PreparedViewExportData> views)
    {
        List<ArtifactLayerPlan> layers = new();
        foreach (string layerName in GetSelectedLayerNames(featureTypes))
        {
            List<ExportLayer> sourceLayers = views
                .Select(view => GetLayerByName(view, layerName))
                .Where(layer => layer != null)
                .Cast<ExportLayer>()
                .ToList();
            if (sourceLayers.Count == 0)
            {
                continue;
            }

            layers.Add(new ArtifactLayerPlan(layerName, MergeLayers(sourceLayers)));
        }

        return layers;
    }

    private static ExportLayer MergeLayers(IReadOnlyList<ExportLayer> sourceLayers)
    {
        ExportLayer first = sourceLayers[0];
        ExportLayer merged = new(first.Name, first.GeometryType, first.Attributes);
        foreach (ExportLayer layer in sourceLayers)
        {
            foreach (IExportFeature feature in layer.Features)
            {
                merged.AddFeature(feature);
            }
        }

        return merged;
    }

    private static IEnumerable<(string LayerName, ExportLayer Layer)> GetLayerEntries(
        ExportFeatureType featureTypes,
        PreparedViewExportData viewData)
    {
        if (featureTypes.HasFlag(ExportFeatureType.Unit) && viewData.UnitLayer != null)
        {
            yield return ("unit", viewData.UnitLayer);
        }

        if (featureTypes.HasFlag(ExportFeatureType.Detail) && viewData.DetailLayer != null)
        {
            yield return ("detail", viewData.DetailLayer);
        }

        if (featureTypes.HasFlag(ExportFeatureType.Opening) && viewData.OpeningLayer != null)
        {
            yield return ("opening", viewData.OpeningLayer);
        }

        if (featureTypes.HasFlag(ExportFeatureType.Level) && viewData.LevelLayer != null)
        {
            yield return ("level", viewData.LevelLayer);
        }
    }

    private static IReadOnlyList<ExportLayer> GetSelectedLayers(ExportFeatureType featureTypes, PreparedViewExportData viewData)
    {
        return GetLayerEntries(featureTypes, viewData)
            .Select(entry => entry.Layer)
            .ToList();
    }

    private static IReadOnlyList<string> GetSelectedLayerNames(ExportFeatureType featureTypes)
    {
        List<string> names = new();
        if (featureTypes.HasFlag(ExportFeatureType.Unit))
        {
            names.Add("unit");
        }

        if (featureTypes.HasFlag(ExportFeatureType.Detail))
        {
            names.Add("detail");
        }

        if (featureTypes.HasFlag(ExportFeatureType.Opening))
        {
            names.Add("opening");
        }

        if (featureTypes.HasFlag(ExportFeatureType.Level))
        {
            names.Add("level");
        }

        return names;
    }

    private static ExportLayer? GetLayerByName(PreparedViewExportData viewData, string layerName)
    {
        return layerName switch
        {
            "unit" => viewData.UnitLayer,
            "detail" => viewData.DetailLayer,
            "opening" => viewData.OpeningLayer,
            "level" => viewData.LevelLayer,
            _ => null,
        };
    }

    private static bool CanReuseArtifact(ExportBaselineSnapshot? baselineSnapshot, ArtifactPlan plan)
    {
        if (baselineSnapshot == null)
        {
            return false;
        }

        ExportBaselineArtifactSnapshot? baselineArtifact = baselineSnapshot.Artifacts.FirstOrDefault(artifact =>
            string.Equals(artifact.ArtifactKey, plan.ArtifactKey, StringComparison.Ordinal) &&
            string.Equals(artifact.PackagingMode, plan.PackagingMode.ToString(), StringComparison.Ordinal) &&
            string.Equals(artifact.OutputFilePath, plan.OutputFilePath, StringComparison.Ordinal));
        return baselineArtifact != null && File.Exists(baselineArtifact.OutputFilePath);
    }

    private static ExportBaselineSnapshot BuildBaselineSnapshot(
        PreparedExportSession session,
        IReadOnlyDictionary<long, ViewChangeDecision> viewDecisions,
        IReadOnlyList<ArtifactPlan> artifactPlans)
    {
        ExportFingerprintBuilder fingerprintBuilder = new();
        Dictionary<long, List<string>> artifactKeysByViewId = artifactPlans
            .SelectMany(plan => plan.ContributingViewIds.Select(viewId => new { viewId, plan.ArtifactKey }))
            .GroupBy(entry => entry.viewId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.ArtifactKey).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList());

        return new ExportBaselineSnapshot
        {
            BaselineKey = session.BaselineKey,
            SourceDocumentKey = session.SourceDocumentKey,
            SourceModelName = session.SourceModelName,
            ProfileName = session.ProfileName,
            ConfigurationFingerprint = BuildConfigurationFingerprint(session, fingerprintBuilder),
            ExportedAtUtc = DateTimeOffset.UtcNow,
            Views = viewDecisions.Values
                .Select(decision => new ExportBaselineViewSnapshot
                {
                    ViewId = decision.ViewData.View.Id.Value,
                    ViewName = decision.ViewData.View.Name,
                    LevelName = decision.ViewData.Level.Name,
                    ContentFingerprint = decision.ContentFingerprint,
                    ArtifactKeys = artifactKeysByViewId.TryGetValue(decision.ViewData.View.Id.Value, out List<string>? artifactKeys)
                        ? artifactKeys
                        : new List<string>(),
                })
                .OrderBy(view => view.ViewName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(view => view.ViewId)
                .ToList(),
            Artifacts = artifactPlans
                .Select(plan => new ExportBaselineArtifactSnapshot
                {
                    ArtifactKey = plan.ArtifactKey,
                    OutputFilePath = plan.OutputFilePath,
                    PackagingMode = plan.PackagingMode.ToString(),
                    ContributingViewIds = plan.ContributingViewIds.OrderBy(id => id).ToList(),
                    LayerNames = plan.LayerNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList(),
                    FeatureCount = plan.FeatureCount,
                })
                .OrderBy(artifact => artifact.ArtifactKey, StringComparer.Ordinal)
                .ToList(),
        };
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

    private sealed class ViewChangeDecision
    {
        public ViewChangeDecision(PreparedViewExportData viewData, string contentFingerprint, bool hasChanges, string? reason)
        {
            ViewData = viewData;
            ContentFingerprint = contentFingerprint;
            HasChanges = hasChanges;
            Reason = reason;
        }

        public PreparedViewExportData ViewData { get; }

        public string ContentFingerprint { get; }

        public bool HasChanges { get; }

        public string? Reason { get; }
    }

    private sealed class ArtifactLayerPlan
    {
        public ArtifactLayerPlan(string layerName, ExportLayer layer)
        {
            LayerName = layerName;
            Layer = layer;
        }

        public string LayerName { get; }

        public ExportLayer Layer { get; }
    }

    private sealed class ArtifactPlan
    {
        public ArtifactPlan(
            string artifactKey,
            string artifactName,
            string outputFilePath,
            PackagingMode packagingMode,
            IReadOnlyList<PreparedViewExportData> views,
            IReadOnlyList<ArtifactLayerPlan> layers)
        {
            ArtifactKey = artifactKey;
            ArtifactName = artifactName;
            OutputFilePath = outputFilePath;
            PackagingMode = packagingMode;
            Views = views;
            Layers = layers;
        }

        public string ArtifactKey { get; }

        public string ArtifactName { get; }

        public string OutputFilePath { get; }

        public PackagingMode PackagingMode { get; }

        public IReadOnlyList<PreparedViewExportData> Views { get; }

        public IReadOnlyList<ArtifactLayerPlan> Layers { get; }

        public bool ShouldWrite { get; set; }

        public IReadOnlyList<long> ContributingViewIds => Views.Select(view => view.View.Id.Value).Distinct().OrderBy(id => id).ToList();

        public IReadOnlyList<string> ContributingViewNames => Views.Select(view => view.View.Name).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

        public IReadOnlyList<string> ContributingLevelNames => Views.Select(view => view.Level.Name).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

        public IReadOnlyList<string> LayerNames => Layers.Select(layer => layer.LayerName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

        public int FeatureCount => Layers.Sum(layer => layer.Layer.Features.Count);

        public ExportArtifactResult ToResult(ArtifactDisposition disposition)
        {
            return new ExportArtifactResult(
                ArtifactKey,
                ArtifactName,
                OutputFilePath,
                PackagingMode,
                disposition,
                ContributingViewIds,
                ContributingViewNames,
                ContributingLevelNames,
                LayerNames,
                FeatureCount);
        }
    }
}
