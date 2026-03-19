using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoExporter.Core.Models;

public static class ImdfUnitCategoryCatalog
{
    private static readonly Dictionary<string, ZoneInfo> OfficialCategoryLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["auditorium"] = new ZoneInfo("auditorium", "D8E8FF", null),
        ["brick"] = new ZoneInfo("brick", "C9856B", null),
        ["classroom"] = new ZoneInfo("classroom", "FFF2B3", null),
        ["column"] = new ZoneInfo("column", "BDBDBD", null),
        ["concrete"] = new ZoneInfo("concrete", "C8C8C8", null),
        ["conferenceroom"] = new ZoneInfo("conferenceroom", "CCE5FF", null),
        ["drywall"] = new ZoneInfo("drywall", "E8E2D8", null),
        ["elevator"] = new ZoneInfo("elevator", "E0E0E0", null),
        ["escalator"] = new ZoneInfo("escalator", "D0D0D0", null),
        ["fieldofplay"] = new ZoneInfo("fieldofplay", "CFE8C6", null),
        ["firstaid"] = new ZoneInfo("firstaid", "FFD3D3", null),
        ["fitnessroom"] = new ZoneInfo("fitnessroom", "E2FFD7", null),
        ["foodservice"] = new ZoneInfo("foodservice", "FFE4C6", null),
        ["footbridge"] = new ZoneInfo("footbridge", "E6F3FF", null),
        ["glass"] = new ZoneInfo("glass", "EAF8FF", null),
        ["huddleroom"] = new ZoneInfo("huddleroom", "DDEBFF", null),
        ["kitchen"] = new ZoneInfo("kitchen", "FFF1D1", null),
        ["laboratory"] = new ZoneInfo("laboratory", "EADFFF", null),
        ["library"] = new ZoneInfo("library", "F7F0D8", null),
        ["lobby"] = new ZoneInfo("lobby", "F4F4D1", null),
        ["lounge"] = new ZoneInfo("lounge", "F5E6FF", null),
        ["mailroom"] = new ZoneInfo("mailroom", "EFEFEF", null),
        ["mothersroom"] = new ZoneInfo("mothersroom", "FFE0F0", null),
        ["movietheater"] = new ZoneInfo("movietheater", "D7D7FF", null),
        ["movingwalkway"] = new ZoneInfo("movingwalkway", "E8F6FF", null),
        ["nonpublic"] = new ZoneInfo("nonpublic", "F3F3F3", null),
        ["office"] = new ZoneInfo("office", "E8F4FF", null),
        ["opentobelow"] = new ZoneInfo("opentobelow", "F8F8F8", null),
        ["parking"] = new ZoneInfo("parking", "E3E3E3", null),
        ["phoneroom"] = new ZoneInfo("phoneroom", "D9EBFF", null),
        ["platform"] = new ZoneInfo("platform", "FFF6F3", null),
        ["privatelounge"] = new ZoneInfo("privatelounge", "E8D9FF", null),
        ["ramp"] = new ZoneInfo("ramp", "E9E9E9", null),
        ["recreation"] = new ZoneInfo("recreation", "E2FFD2", null),
        ["restroom"] = new ZoneInfo("restroom", "D9FFD9", null),
        ["restroom.family"] = new ZoneInfo("restroom.family", "D9FFD9", null),
        ["restroom.female"] = new ZoneInfo("restroom.female", "FFA4A4", null),
        ["restroom.female.wheelchair"] = new ZoneInfo("restroom.female.wheelchair", "FFB8B8", null),
        ["restroom.male"] = new ZoneInfo("restroom.male", "BBD2EF", null),
        ["restroom.male.wheelchair"] = new ZoneInfo("restroom.male.wheelchair", "CDE0F6", null),
        ["restroom.transgender"] = new ZoneInfo("restroom.transgender", "E3D4FF", null),
        ["restroom.transgender.wheelchair"] = new ZoneInfo("restroom.transgender.wheelchair", "EDE2FF", null),
        ["restroom.unisex"] = new ZoneInfo("restroom.unisex", "D9FFD9", null),
        ["restroom.unisex.wheelchair"] = new ZoneInfo("restroom.unisex.wheelchair", "E8FFE8", null),
        ["restroom.wheelchair"] = new ZoneInfo("restroom.wheelchair", "E8FFE8", null),
        ["road"] = new ZoneInfo("road", "E4E5E5", null),
        ["room"] = new ZoneInfo("room", "F7F7F7", null),
        ["serverroom"] = new ZoneInfo("serverroom", "D8D8E8", null),
        ["shower"] = new ZoneInfo("shower", "DFF7FF", null),
        ["smokingarea"] = new ZoneInfo("smokingarea", "E2D9D9", null),
        ["stairs"] = new ZoneInfo("stairs", "C0C0C0", null),
        ["steps"] = new ZoneInfo("steps", "C6C6C6", null),
        ["storage"] = new ZoneInfo("storage", "EFE6D8", null),
        ["structure"] = new ZoneInfo("structure", "C2C2C2", null),
        ["terrace"] = new ZoneInfo("terrace", "F6F2E2", null),
        ["theater"] = new ZoneInfo("theater", "DDD8FF", null),
        ["unenclosedarea"] = new ZoneInfo("unenclosedarea", "FAFAFA", null),
        ["unspecified"] = new ZoneInfo("unspecified", ZoneCatalog.DefaultColor, null),
        ["vegetation"] = new ZoneInfo("vegetation", "D8F0D2", null),
        ["waitingroom"] = new ZoneInfo("waitingroom", "BABABA", null),
        ["walkway"] = new ZoneInfo("walkway", "FFFFFF", null),
        ["walkway.island"] = new ZoneInfo("walkway.island", "F2F2F2", null),
        ["wood"] = new ZoneInfo("wood", "C89E6E", null),
    };

    private static readonly Dictionary<string, ZoneInfo> LegacyCategoryLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["retail"] = new ZoneInfo("retail", "E1F3F9", null),
        ["information"] = new ZoneInfo("information", "EFEFF9", null),
        ["ticketing"] = new ZoneInfo("ticketing", "C2E389", null),
        ["outdoors"] = new ZoneInfo("outdoors", "FFFFFF", null),
    };

    public static bool TryGetInfo(string? category, out ZoneInfo info)
    {
        string normalized = Normalize(category);
        if (normalized.Length > 0 && OfficialCategoryLookup.TryGetValue(normalized, out ZoneInfo? official))
        {
            info = official;
            return true;
        }

        if (normalized.Length > 0 && LegacyCategoryLookup.TryGetValue(normalized, out ZoneInfo? legacy))
        {
            info = legacy;
            return true;
        }

        info = ZoneInfo.Default();
        return false;
    }

    public static bool IsOfficialCategory(string? category)
    {
        string normalized = Normalize(category);
        return normalized.Length > 0 && OfficialCategoryLookup.ContainsKey(normalized);
    }

    public static bool IsLegacyCategory(string? category)
    {
        string normalized = Normalize(category);
        return normalized.Length > 0 && LegacyCategoryLookup.ContainsKey(normalized);
    }

    public static IReadOnlyList<string> GetCategories(bool includeLegacy, bool includeUnspecified)
    {
        List<string> categories = OfficialCategoryLookup.Keys
            .Where(key => includeUnspecified || !string.Equals(key, "unspecified", StringComparison.OrdinalIgnoreCase))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (includeLegacy)
        {
            categories.AddRange(LegacyCategoryLookup.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase));
        }

        return categories
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
