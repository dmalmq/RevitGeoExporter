using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Core.Utilities;

namespace RevitGeoExporter.Export;

public sealed class ExportPreviewService
{
    private const string UnitStrokeColorHex = "4A5568";
    private const string OpeningStrokeColorHex = "C45100";

    private readonly FloorExportDataPreparer _preparer;
    private readonly PreviewExportMetadataProvider _metadataProvider;
    private readonly PreviewPaletteResolver _paletteResolver;
    private readonly FloorCategoryOverrideStore _floorCategoryOverrideStore;
    private readonly FamilyCategoryOverrideStore _familyCategoryOverrideStore;
    private readonly AcceptedOpeningFamilyStore _acceptedOpeningFamilyStore;
    private readonly PreviewFloorAssignmentSession _assignmentSession;
    private readonly IReadOnlyList<string> _loadWarnings;
    private readonly string _projectKey;
    private readonly IReadOnlyList<string> _supportedFloorCategories;
    private readonly IReadOnlyDictionary<string, string> _familyCategoryOverrides;
    private readonly IReadOnlyList<string> _acceptedOpeningFamilies;

    public ExportPreviewService(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        ZoneCatalog zoneCatalog = ZoneCatalog.CreateDefault();
        _floorCategoryOverrideStore = new FloorCategoryOverrideStore();
        _familyCategoryOverrideStore = new FamilyCategoryOverrideStore();
        _acceptedOpeningFamilyStore = new AcceptedOpeningFamilyStore();
        _projectKey = DocumentProjectKeyBuilder.Create(document);
        LoadResult<IReadOnlyDictionary<string, string>> overrideLoad =
            _floorCategoryOverrideStore.LoadWithDiagnostics(_projectKey);
        LoadResult<IReadOnlyDictionary<string, string>> familyOverrideLoad =
            _familyCategoryOverrideStore.LoadWithDiagnostics(_projectKey);
        LoadResult<IReadOnlyList<string>> acceptedOpeningLoad =
            _acceptedOpeningFamilyStore.LoadWithDiagnostics(_projectKey);
        _assignmentSession = new PreviewFloorAssignmentSession(overrideLoad.Value);
        _familyCategoryOverrides = familyOverrideLoad.Value;
        _acceptedOpeningFamilies = acceptedOpeningLoad.Value;
        _loadWarnings = overrideLoad.Warnings
            .Concat(familyOverrideLoad.Warnings)
            .Concat(acceptedOpeningLoad.Warnings)
            .ToList();
        _preparer = new FloorExportDataPreparer(document, zoneCatalog);
        _metadataProvider = new PreviewExportMetadataProvider();
        _paletteResolver = new PreviewPaletteResolver();
        _supportedFloorCategories = new FloorCategoryResolver(zoneCatalog).SupportedCategories;
    }

    public IReadOnlyList<string> GetSupportedFloorCategories()
    {
        return _supportedFloorCategories;
    }

    public bool HasPendingFloorCategoryChanges => _assignmentSession.HasPendingChanges;

    public void StageFloorCategoryOverride(string floorTypeName, string category)
    {
        _assignmentSession.StageOverride(floorTypeName, category);
    }

    public void StageClearFloorCategoryOverride(string floorTypeName)
    {
        _assignmentSession.StageClearOverride(floorTypeName);
    }

    public void ApplyPendingFloorCategoryOverrides()
    {
        if (!_assignmentSession.HasPendingChanges)
        {
            return;
        }

        IReadOnlyDictionary<string, string> savedOverrides = _assignmentSession.ApplyPendingChanges();
        _floorCategoryOverrideStore.Save(_projectKey, savedOverrides);
    }

    public void DiscardPendingFloorCategoryOverrides()
    {
        _assignmentSession.DiscardPendingChanges();
    }

    public PreviewViewData PrepareView(ViewPlan view, ExportFeatureType featureTypes)
    {
        if (view is null)
        {
            throw new ArgumentNullException(nameof(view));
        }

        ExportFeatureType previewFeatureTypes = featureTypes & (ExportFeatureType.Unit | ExportFeatureType.Opening);
        if (previewFeatureTypes == ExportFeatureType.None)
        {
            throw new ArgumentException("Preview requires unit and/or opening feature types.", nameof(featureTypes));
        }

        PreparedViewExportData prepared = _preparer.PrepareView(
            view,
            previewFeatureTypes,
            _metadataProvider,
            new FloorExportPreparationOptions
            {
                FloorCategoryOverrides = _assignmentSession.GetEffectiveOverrides(),
                FamilyCategoryOverrides = _familyCategoryOverrides,
                AcceptedOpeningFamilies = _acceptedOpeningFamilies,
                InitialWarnings = _loadWarnings,
            });
        List<PreviewFeatureData> features = new();

        if (prepared.UnitLayer != null)
        {
            foreach (ExportPolygon feature in prepared.UnitLayer.Features.OfType<ExportPolygon>())
            {
                string category = ReadString(feature.Attributes, "category");
                string? fallbackFillColor = ReadString(feature.Attributes, "preview_fill_color");
                features.Add(
                    new PreviewFeatureData(
                        ExportFeatureType.Unit,
                        feature,
                        ReadNullableLong(feature.Attributes, "source_element_id"),
                        ReadString(feature.Attributes, "id"),
                        category,
                        ReadString(feature.Attributes, "restrict"),
                        ReadString(feature.Attributes, "name"),
                        _paletteResolver.ResolveFillColor(category, fallbackFillColor),
                        UnitStrokeColorHex,
                        ReadBool(feature.Attributes, "is_floor_derived"),
                        ReadNullableString(feature.Attributes, "source_floor_type_name"),
                        ReadNullableString(feature.Attributes, "parsed_zone_candidate"),
                        ReadBool(feature.Attributes, "is_unassigned"),
                        ReadResolutionSource(feature.Attributes, "category_resolution_source")));
            }
        }

        if (prepared.OpeningLayer != null)
        {
            foreach (ExportLineString feature in prepared.OpeningLayer.Features.OfType<ExportLineString>())
            {
                features.Add(
                    new PreviewFeatureData(
                        ExportFeatureType.Opening,
                        feature,
                        ReadNullableLong(feature.Attributes, "element_id"),
                        ReadString(feature.Attributes, "id"),
                        ReadString(feature.Attributes, "category"),
                        null,
                        null,
                        OpeningStrokeColorHex,
                        OpeningStrokeColorHex));
            }
        }

        Bounds2D bounds = FeatureBoundsCalculator.FromFeatures(features.Select(x => x.Feature));
        List<PreviewUnassignedFloorGroup> unassignedFloors = features
            .Where(feature => feature.FeatureType == ExportFeatureType.Unit &&
                              feature.IsFloorDerived &&
                              feature.IsUnassignedFloor &&
                              !string.IsNullOrWhiteSpace(feature.FloorTypeName))
            .GroupBy(feature => feature.FloorTypeName!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PreviewUnassignedFloorGroup(
                group.Key,
                group.Select(feature => feature.ParsedZoneCandidate)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                group.Count()))
            .ToList();
        return new PreviewViewData(
            prepared.View.Id.Value,
            prepared.View.Name,
            prepared.Level.Name,
            features,
            unassignedFloors,
            prepared.Warnings,
            bounds);
    }

    private static string ReadString(IReadOnlyDictionary<string, object?> attributes, string key)
    {
        if (attributes.TryGetValue(key, out object? value))
        {
            return value?.ToString()?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string? ReadNullableString(IReadOnlyDictionary<string, object?> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out object? value) || value == null)
        {
            return null;
        }

        string trimmed = value.ToString()?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool ReadBool(IReadOnlyDictionary<string, object?> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out object? value) || value == null)
        {
            return false;
        }

        return value switch
        {
            bool boolValue => boolValue,
            string stringValue when bool.TryParse(stringValue, out bool parsed) => parsed,
            _ => false,
        };
    }

    private static long? ReadNullableLong(IReadOnlyDictionary<string, object?> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out object? value) || value == null)
        {
            return null;
        }

        return value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            string stringValue when long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) => parsed,
            _ => null,
        };
    }

    private static FloorCategoryResolutionSource? ReadResolutionSource(
        IReadOnlyDictionary<string, object?> attributes,
        string key)
    {
        string? value = ReadNullableString(attributes, key);
        return Enum.TryParse(value, ignoreCase: true, out FloorCategoryResolutionSource parsed)
            ? parsed
            : null;
    }
}
