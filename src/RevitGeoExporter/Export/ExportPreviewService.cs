using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Core.Schema;
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
    private readonly RoomCategoryOverrideStore _roomCategoryOverrideStore;
    private readonly FamilyCategoryOverrideStore _familyCategoryOverrideStore;
    private readonly AcceptedOpeningFamilyStore _acceptedOpeningFamilyStore;
    private readonly PreviewCategoryAssignmentSession _assignmentSession;
    private readonly IReadOnlyList<string> _loadWarnings;
    private readonly string _projectKey;
    private readonly IReadOnlyList<string> _supportedCategories;
    private readonly IReadOnlyDictionary<string, string> _familyCategoryOverrides;
    private readonly IReadOnlyList<string> _acceptedOpeningFamilies;
    private readonly GeometryRepairOptions _geometryRepairOptions;
    private readonly UnitSource _unitSource;
    private readonly string _roomCategoryParameterName;
    private readonly LinkExportOptions _linkExportOptions;
    private readonly SchemaProfile _activeSchemaProfile;

    public ExportPreviewService(
        Document document,
        UnitSource unitSource = UnitSource.Floors,
        string roomCategoryParameterName = "Name",
        GeometryRepairOptions? geometryRepairOptions = null,
        LinkExportOptions? linkExportOptions = null,
        SchemaProfile? activeSchemaProfile = null)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        ZoneCatalog zoneCatalog = ZoneCatalog.CreateDefault();
        _floorCategoryOverrideStore = new FloorCategoryOverrideStore();
        _roomCategoryOverrideStore = new RoomCategoryOverrideStore();
        _familyCategoryOverrideStore = new FamilyCategoryOverrideStore();
        _acceptedOpeningFamilyStore = new AcceptedOpeningFamilyStore();
        _unitSource = unitSource;
        _roomCategoryParameterName = string.IsNullOrWhiteSpace(roomCategoryParameterName) ? "Name" : roomCategoryParameterName.Trim();
        _projectKey = DocumentProjectKeyBuilder.Create(document);
        LoadResult<IReadOnlyDictionary<string, string>> floorOverrideLoad =
            _floorCategoryOverrideStore.LoadWithDiagnostics(_projectKey);
        LoadResult<IReadOnlyDictionary<string, string>> roomOverrideLoad =
            _roomCategoryOverrideStore.LoadWithDiagnostics(_projectKey);
        LoadResult<IReadOnlyDictionary<string, string>> familyOverrideLoad =
            _familyCategoryOverrideStore.LoadWithDiagnostics(_projectKey);
        LoadResult<IReadOnlyList<string>> acceptedOpeningLoad =
            _acceptedOpeningFamilyStore.LoadWithDiagnostics(_projectKey);
        _assignmentSession = new PreviewCategoryAssignmentSession(
            unitSource == UnitSource.Rooms ? roomOverrideLoad.Value : floorOverrideLoad.Value);
        _familyCategoryOverrides = familyOverrideLoad.Value;
        _acceptedOpeningFamilies = acceptedOpeningLoad.Value;
        _loadWarnings = floorOverrideLoad.Warnings
            .Concat(roomOverrideLoad.Warnings)
            .Concat(familyOverrideLoad.Warnings)
            .Concat(acceptedOpeningLoad.Warnings)
            .ToList();
        _preparer = new FloorExportDataPreparer(document, zoneCatalog);
        _metadataProvider = new PreviewExportMetadataProvider();
        _paletteResolver = new PreviewPaletteResolver();
        _supportedCategories = unitSource == UnitSource.Rooms
            ? new RoomCategoryResolver(zoneCatalog).SupportedCategories
            : new FloorCategoryResolver(zoneCatalog).SupportedCategories;
        _geometryRepairOptions = (geometryRepairOptions ?? new GeometryRepairOptions()).GetEffectiveOptions();
        _linkExportOptions = linkExportOptions?.Clone() ?? new LinkExportOptions();
        _activeSchemaProfile = activeSchemaProfile?.Clone() ?? SchemaProfile.CreateCoreProfile();
    }

    public IReadOnlyList<string> GetSupportedFloorCategories()
    {
        return _supportedCategories;
    }

    public string GetAssignmentSourceLabel()
    {
        return _unitSource == UnitSource.Rooms
            ? $"Room Values ({_roomCategoryParameterName})"
            : "Floor Types";
    }

    public bool HasPendingFloorCategoryChanges => _assignmentSession.HasPendingChanges;

    public void StageFloorCategoryOverride(string key, string category)
    {
        _assignmentSession.StageOverride(key, category);
    }

    public void StageClearFloorCategoryOverride(string key)
    {
        _assignmentSession.StageClearOverride(key);
    }

    public void ApplyPendingFloorCategoryOverrides()
    {
        if (!_assignmentSession.HasPendingChanges)
        {
            return;
        }

        IReadOnlyDictionary<string, string> savedOverrides = _assignmentSession.ApplyPendingChanges();
        if (_unitSource == UnitSource.Rooms)
        {
            _roomCategoryOverrideStore.Save(_projectKey, savedOverrides);
            return;
        }

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

        ExportFeatureType previewFeatureTypes = featureTypes & ExportFeatureType.All;
        if (previewFeatureTypes == ExportFeatureType.None)
        {
            throw new ArgumentException("Preview requires at least one feature type.", nameof(featureTypes));
        }

        PreparedViewExportData prepared = _preparer.PrepareView(
            view,
            previewFeatureTypes,
            _metadataProvider,
            new FloorExportPreparationOptions
            {
                FloorCategoryOverrides = _unitSource == UnitSource.Floors ? _assignmentSession.GetEffectiveOverrides() : null,
                RoomCategoryOverrides = _unitSource == UnitSource.Rooms ? _assignmentSession.GetEffectiveOverrides() : null,
                FamilyCategoryOverrides = _familyCategoryOverrides,
                AcceptedOpeningFamilies = _acceptedOpeningFamilies,
                InitialWarnings = _loadWarnings,
                GeometryRepairOptions = _geometryRepairOptions,
                UnitSource = _unitSource,
                RoomCategoryParameterName = _roomCategoryParameterName,
                LinkExportOptions = _linkExportOptions,
                ActiveSchemaProfile = _activeSchemaProfile,
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
                        ReadNullableString(feature.Attributes, "source_label"),
                        _paletteResolver.ResolveFillColor(category, fallbackFillColor),
                        UnitStrokeColorHex,
                        ReadNullableString(feature.Attributes, "assignment_source_kind"),
                        ReadNullableString(feature.Attributes, "assignment_mapping_key") ?? ReadNullableString(feature.Attributes, "source_floor_type_name"),
                        ReadNullableString(feature.Attributes, "assignment_parsed_candidate") ?? ReadNullableString(feature.Attributes, "parsed_zone_candidate"),
                        ReadNullableString(feature.Attributes, "assignment_parameter_name"),
                        ReadBool(feature.Attributes, "is_unassigned"),
                        ReadResolutionSource(feature.Attributes, "category_resolution_source"),
                        ReadBool(feature.Attributes, "is_unassigned")));
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
                        ReadNullableString(feature.Attributes, "source_label"),
                        OpeningStrokeColorHex,
                        OpeningStrokeColorHex,
                        hasWarning: !ReadBool(feature.Attributes, "is_snapped_to_outline", defaultValue: true)));
            }
        }

        if (prepared.DetailLayer != null)
        {
            foreach (ExportLineString feature in prepared.DetailLayer.Features.OfType<ExportLineString>())
            {
                features.Add(
                    new PreviewFeatureData(
                        ExportFeatureType.Detail,
                        feature,
                        ReadNullableLong(feature.Attributes, "element_id"),
                        ReadString(feature.Attributes, "id"),
                        "detail",
                        null,
                        null,
                        ReadNullableString(feature.Attributes, "source_label"),
                        "666666",
                        "666666"));
            }
        }

        if (prepared.LevelLayer != null)
        {
            foreach (ExportPolygon feature in prepared.LevelLayer.Features.OfType<ExportPolygon>())
            {
                features.Add(
                    new PreviewFeatureData(
                        ExportFeatureType.Level,
                        feature,
                        null,
                        ReadString(feature.Attributes, "id"),
                        "level",
                        null,
                        ReadString(feature.Attributes, "name"),
                        ReadNullableString(feature.Attributes, "name"),
                        "DDE7F0",
                        "607D8B"));
            }
        }

        Bounds2D bounds = FeatureBoundsCalculator.FromFeatures(features.Select(x => x.Feature));
        List<PreviewUnassignedFloorGroup> unassignedFloors = features
            .Where(feature => feature.FeatureType == ExportFeatureType.Unit &&
                              feature.IsUnassigned &&
                              !string.IsNullOrWhiteSpace(feature.AssignmentMappingKey))
            .GroupBy(feature => $"{feature.AssignmentSourceKind}|{feature.AssignmentParameterName}|{feature.AssignmentMappingKey}", StringComparer.Ordinal)
            .OrderBy(group => group.First().AssignmentMappingKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                PreviewFeatureData first = group.First();
                return new PreviewUnassignedFloorGroup(
                    first.AssignmentMappingKey!,
                    group.Select(feature => feature.AssignmentParsedCandidate)
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                    group.Count(),
                    first.AssignmentSourceKind ?? (_unitSource == UnitSource.Rooms ? "room" : "floor"),
                    first.AssignmentParameterName);
            })
            .ToList();
        List<string> sourceLabels = features
            .Select(feature => feature.SourceLabel)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PreviewViewData(
            prepared.View.Id.Value,
            prepared.View.Name,
            prepared.Level.Name,
            features,
            unassignedFloors,
            prepared.Warnings,
            sourceLabels,
            bounds,
            _unitSource,
            _roomCategoryParameterName);
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

    private static bool ReadBool(IReadOnlyDictionary<string, object?> attributes, string key, bool defaultValue = false)
    {
        if (!attributes.TryGetValue(key, out object? value) || value == null)
        {
            return defaultValue;
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
        if (!attributes.TryGetValue(key, out object? value) || value == null)
        {
            return null;
        }

        return Enum.TryParse(value.ToString(), ignoreCase: true, out FloorCategoryResolutionSource parsed)
            ? parsed
            : null;
    }
}
