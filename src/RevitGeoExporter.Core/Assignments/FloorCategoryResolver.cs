using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Assignments;

public sealed class FloorCategoryResolver
{
    private readonly ZoneCatalog _zoneCatalog;
    private readonly Dictionary<string, string> _floorTypeOverrides;
    private readonly IReadOnlyList<string> _supportedCategories;

    public FloorCategoryResolver(
        ZoneCatalog zoneCatalog,
        IReadOnlyDictionary<string, string>? floorTypeOverrides = null)
    {
        _zoneCatalog = zoneCatalog ?? throw new ArgumentNullException(nameof(zoneCatalog));
        _floorTypeOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in floorTypeOverrides ?? EmptyOverrides)
        {
            string floorTypeName = NormalizeFloorTypeName(entry.Key);
            string category = NormalizeCategory(entry.Value);
            if (floorTypeName.Length == 0 || category.Length == 0)
            {
                continue;
            }

            _floorTypeOverrides[floorTypeName] = category;
        }

        _supportedCategories = BuildSupportedCategories(zoneCatalog);
    }

    public IReadOnlyList<string> SupportedCategories => _supportedCategories;

    public ResolvedFloorCategory Resolve(string rawFloorTypeName, string? parsedZoneCandidate)
    {
        string normalizedFloorTypeName = NormalizeFloorTypeName(rawFloorTypeName);
        string normalizedCandidate = NormalizeFloorTypeName(parsedZoneCandidate);

        if (normalizedFloorTypeName.Length > 0 &&
            _floorTypeOverrides.TryGetValue(normalizedFloorTypeName, out string overrideCategory) &&
            _zoneCatalog.TryGetCategoryInfo(overrideCategory, out ZoneInfo overrideInfo))
        {
            return new ResolvedFloorCategory(
                normalizedFloorTypeName,
                normalizedCandidate.Length == 0 ? null : normalizedCandidate,
                overrideInfo,
                FloorCategoryResolutionSource.Override,
                isUnassigned: false);
        }

        if (normalizedCandidate.Length > 0 &&
            _zoneCatalog.TryGetZoneInfo(normalizedCandidate, out ZoneInfo catalogInfo))
        {
            return new ResolvedFloorCategory(
                normalizedFloorTypeName,
                normalizedCandidate,
                catalogInfo,
                FloorCategoryResolutionSource.Catalog,
                isUnassigned: false);
        }

        return new ResolvedFloorCategory(
            normalizedFloorTypeName,
            normalizedCandidate.Length == 0 ? null : normalizedCandidate,
            _zoneCatalog.DefaultZoneInfo,
            FloorCategoryResolutionSource.FallbackUnspecified,
            isUnassigned: true);
    }

    private static IReadOnlyList<string> BuildSupportedCategories(ZoneCatalog zoneCatalog)
    {
        HashSet<string> categories = new(StringComparer.OrdinalIgnoreCase);
        foreach (ZoneInfo zoneInfo in zoneCatalog.ZoneLookup.Values)
        {
            string category = NormalizeCategory(zoneInfo.Category);
            if (category.Length == 0 ||
                string.Equals(category, "unspecified", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, "stairs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, "escalator", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, "elevator", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            categories.Add(category);
        }

        return categories
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeFloorTypeName(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value!.Trim();
    }

    private static string NormalizeCategory(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value!.Trim();
    }

    private static IReadOnlyDictionary<string, string> EmptyOverrides =>
        new Dictionary<string, string>(StringComparer.Ordinal);
}
