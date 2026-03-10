using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Assignments;

public sealed class RoomCategoryResolver
{
    private readonly ZoneCatalog _zoneCatalog;
    private readonly Dictionary<string, string> _roomValueOverrides;
    private readonly IReadOnlyList<string> _supportedCategories;

    public RoomCategoryResolver(
        ZoneCatalog zoneCatalog,
        IReadOnlyDictionary<string, string>? roomValueOverrides = null)
    {
        _zoneCatalog = zoneCatalog ?? throw new ArgumentNullException(nameof(zoneCatalog));
        _roomValueOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in roomValueOverrides ?? EmptyOverrides)
        {
            string roomValue = NormalizeRoomValue(entry.Key);
            string category = NormalizeCategory(entry.Value);
            if (roomValue.Length == 0 || category.Length == 0)
            {
                continue;
            }

            _roomValueOverrides[roomValue] = category;
        }

        _supportedCategories = ImdfUnitCategoryCatalog.GetCategories(includeLegacy: true, includeUnspecified: false)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> SupportedCategories => _supportedCategories;

    public ResolvedMappingCategory Resolve(string roomValue, string parameterName)
    {
        string normalizedRoomValue = NormalizeRoomValue(roomValue);

        if (normalizedRoomValue.Length > 0 &&
            _roomValueOverrides.TryGetValue(normalizedRoomValue, out string overrideCategory) &&
            _zoneCatalog.TryGetCategoryInfo(overrideCategory, out ZoneInfo overrideInfo))
        {
            return new ResolvedMappingCategory(
                "room",
                normalizedRoomValue,
                normalizedRoomValue,
                parameterName,
                overrideInfo,
                FloorCategoryResolutionSource.Override,
                isUnassigned: false);
        }

        if (normalizedRoomValue.Length > 0 &&
            _zoneCatalog.TryGetZoneInfo(normalizedRoomValue, out ZoneInfo catalogInfo))
        {
            return new ResolvedMappingCategory(
                "room",
                normalizedRoomValue,
                normalizedRoomValue,
                parameterName,
                catalogInfo,
                FloorCategoryResolutionSource.Catalog,
                isUnassigned: false);
        }

        return new ResolvedMappingCategory(
            "room",
            normalizedRoomValue,
            normalizedRoomValue.Length == 0 ? null : normalizedRoomValue,
            parameterName,
            _zoneCatalog.DefaultZoneInfo,
            FloorCategoryResolutionSource.FallbackUnspecified,
            isUnassigned: true);
    }

    private static string NormalizeRoomValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string NormalizeCategory(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static IReadOnlyDictionary<string, string> EmptyOverrides =>
        new Dictionary<string, string>(StringComparer.Ordinal);
}
