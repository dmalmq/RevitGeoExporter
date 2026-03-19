using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitGeoExporter.Core.Models;

public sealed class ZoneCatalog
{
    public const string DefaultColor = "CCCCCC";

    private readonly Dictionary<string, ZoneInfo> _zoneLookup;
    private readonly Dictionary<string, ZoneInfo> _familyLookup;
    private readonly Dictionary<string, ZoneInfo> _categoryLookup;

    public ZoneCatalog(
        IReadOnlyDictionary<string, ZoneInfo> zoneLookup,
        IReadOnlyDictionary<string, ZoneInfo>? familyLookup = null,
        ZoneInfo? defaultZoneInfo = null,
        ZoneInfo? stairsDefault = null)
    {
        if (zoneLookup is null)
        {
            throw new ArgumentNullException(nameof(zoneLookup));
        }

        _zoneLookup = new Dictionary<string, ZoneInfo>(StringComparer.Ordinal);
        _categoryLookup = new Dictionary<string, ZoneInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, ZoneInfo> entry in zoneLookup)
        {
            string zoneName = NormalizeKey(entry.Key);
            if (zoneName.Length == 0 || entry.Value is null)
            {
                continue;
            }

            ZoneInfo normalized = entry.Value.Normalized();
            _zoneLookup[zoneName] = normalized;
            RegisterCategoryInfo(normalized);
        }

        _familyLookup = new Dictionary<string, ZoneInfo>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ZoneInfo> entry in familyLookup ?? EmptyFamilyLookup)
        {
            string familyName = NormalizeKey(entry.Key);
            if (familyName.Length == 0 || entry.Value is null)
            {
                continue;
            }

            _familyLookup[familyName] = entry.Value.Normalized();
        }

        DefaultZoneInfo = (defaultZoneInfo ?? ZoneInfo.Default()).Normalized();
        StairsDefault = (stairsDefault ?? ZoneInfo.StairsDefault()).Normalized();
    }

    public ZoneInfo DefaultZoneInfo { get; }

    public ZoneInfo StairsDefault { get; }

    public IReadOnlyDictionary<string, ZoneInfo> ZoneLookup => _zoneLookup;

    public IReadOnlyDictionary<string, ZoneInfo> FamilyLookup => _familyLookup;

    public static ZoneCatalog CreateDefault()
    {
        Dictionary<string, ZoneInfo> zones = new(StringComparer.Ordinal);

        AddZoneAliases(
            zones,
            new ZoneInfo("walkway", "FFFFFF", "rachi_gai"),
            "ラチ外コンコース",
            "改札外コンコース",
            "Circulation (outside fare gates)");

        AddZoneAliases(
            zones,
            new ZoneInfo("walkway", "FEFEF2", "rachi_nai"),
            "ラチE��Eコンコース",
            "改札冁E�E��E�ンコース",
            "Circulation (inside fare gates)");

        AddZoneAliases(
            zones,
            new ZoneInfo("walkway", "EFFAED", "rachi_nai"),
            "ラチE��Eコンコース(JR東新幹緁E",
            "改札冁E�E��E�ンコース(JR東新幹緁E",
            "Circulation (JR East Shinkansen)");

        AddZoneAliases(
            zones,
            new ZoneInfo("walkway", "FFF4E0", "rachi_nai"),
            "ラチE��Eコンコース(JR東海新幹緁E",
            "改札冁E�E��E�ンコース(JR東海新幹緁E",
            "Circulation (JR Central Shinkansen)");

        AddZoneAliases(
            zones,
            new ZoneInfo("retail", "E1F3F9", "rachi_nai"),
            "ラチE��E店�E",
            "改札冁E�E��E��E�E",
            "Retail (inside fare gates)");

        AddZoneAliases(
            zones,
            new ZoneInfo("retail", "F3FCFF", "rachi_gai"),
            "ラチ外店�E",
            "改札外店�E",
            "Retail (outside fare gates)");

        AddZoneAliases(
            zones,
            new ZoneInfo("platform", "F9E8E1", "rachi_nai"),
            "新幹線�Eーム",
            "Platform (Shinkansen)");

        AddZoneAliases(
            zones,
            new ZoneInfo("platform", "FFF6F3", "rachi_nai"),
            "在来線�Eーム",
            "Platform (conventional lines)");

        AddZoneAliases(
            zones,
            new ZoneInfo("nonpublic", "F3F3F3", null),
            "Other",
            "Miscellaneous",
            "Other / miscellaneous");

        AddZoneAliases(
            zones,
            new ZoneInfo("information", "EFEFF9", null),
            "案�E所",
            "案�E",
            "Information desk");

        AddZoneAliases(
            zones,
            new ZoneInfo("ticketing", "A3CA7F", "rachi_gai"),
            "みどり�E窓口",
            "緑�E窓口",
            "Ticket office (Midori no Madoguchi)");

        AddZoneAliases(
            zones,
            new ZoneInfo("road", "E4E5E5", null),
            "道路",
            "Road");

        AddZoneAliases(
            zones,
            new ZoneInfo("outdoors", "FFFFFF", null),
            "Exterior",
            "Outdoor",
            "Roof",
            "Grounds",
            "Exterior / grounds");

        AddZoneAliases(
            zones,
            new ZoneInfo("restroom.male", "BBD2EF", null),
            "男子トイレ",
            "Men's restroom");

        AddZoneAliases(
            zones,
            new ZoneInfo("restroom.female", "FFA4A4", null),
            "女子トイレ",
            "Women's restroom");

        AddZoneAliases(
            zones,
            new ZoneInfo("restroom.unisex", "D9FFD9", null),
            "多目皁E�E��E�イレ",
            "Accessible / multipurpose restroom");

        AddZoneAliases(
            zones,
            new ZoneInfo("ticketing", "C2E389", null),
            "券売機室",
            "Ticket machine area");

        AddZoneAliases(
            zones,
            new ZoneInfo("waitingroom", "BABABA", null),
            "征E�E��E�室",
            "Waiting room");

        AddZoneAliases(
            zones,
            new ZoneInfo("retail", "979797", "rachi_gai"),
            "Retail annex",
            "Commercial support",
            "Retail (external commercial facilities)");

        Dictionary<string, ZoneInfo> families = new(StringComparer.Ordinal)
        {
            ["j EV"] = new ZoneInfo("elevator", "E0E0E0", null),
            ["j エスカレータ-lightweight"] = new ZoneInfo("escalator", "D0D0D0", null),
            ["j エスカレーター-lightweight"] = new ZoneInfo("escalator", "D0D0D0", null),
        };

        return new ZoneCatalog(zones, families);
    }

    public static ZoneCatalog FromJsonFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("JSON file path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Zone catalog file was not found.", path);
        }

        string json = File.ReadAllText(path);
        JToken token = JToken.Parse(json);
        if (token.Type == JTokenType.Object)
        {
            IReadOnlyDictionary<string, ZoneInfo> zoneLookup = ParseZoneLookup(token["zones"]);
            IReadOnlyDictionary<string, ZoneInfo> familyLookup = ParseZoneLookup(token["families"]);
            ZoneInfo? defaultZone = token["default"]?.ToObject<ZoneInfo>();
            ZoneInfo? stairsDefault = token["stairsDefault"]?.ToObject<ZoneInfo>();
            return new ZoneCatalog(zoneLookup, familyLookup, defaultZone, stairsDefault);
        }

        Dictionary<string, ZoneInfo>? direct = JsonConvert.DeserializeObject<Dictionary<string, ZoneInfo>>(json);
        return new ZoneCatalog(direct ?? new Dictionary<string, ZoneInfo>(StringComparer.Ordinal));
    }

    public bool TryGetZoneInfo(string zoneName, out ZoneInfo info)
    {
        if (!string.IsNullOrWhiteSpace(zoneName) &&
            _zoneLookup.TryGetValue(NormalizeKey(zoneName), out ZoneInfo? known))
        {
            info = known;
            return true;
        }

        if (ImdfUnitCategoryCatalog.TryGetInfo(zoneName, out ZoneInfo imdfInfo))
        {
            info = imdfInfo;
            return true;
        }

        info = DefaultZoneInfo;
        return false;
    }

    public ZoneInfo GetZoneInfoOrDefault(string zoneName)
    {
        return TryGetZoneInfo(zoneName, out ZoneInfo info) ? info : DefaultZoneInfo;
    }

    public bool TryGetFamilyInfo(string familyName, out ZoneInfo info)
    {
        if (!string.IsNullOrWhiteSpace(familyName) &&
            _familyLookup.TryGetValue(NormalizeKey(familyName), out ZoneInfo? known))
        {
            info = known;
            return true;
        }

        info = DefaultZoneInfo;
        return false;
    }

    public bool TryGetCategoryInfo(string category, out ZoneInfo info)
    {
        if (!string.IsNullOrWhiteSpace(category) &&
            _categoryLookup.TryGetValue(NormalizeKey(category), out ZoneInfo? known))
        {
            info = known;
            return true;
        }

        if (ImdfUnitCategoryCatalog.TryGetInfo(category, out ZoneInfo imdfInfo))
        {
            info = imdfInfo;
            return true;
        }

        info = DefaultZoneInfo;
        return false;
    }

    public bool TryGetColor(string zoneName, out string color)
    {
        if (TryGetZoneInfo(zoneName, out ZoneInfo info))
        {
            color = info.FillColor;
            return true;
        }

        color = DefaultZoneInfo.FillColor;
        return false;
    }

    public string GetColorOrDefault(string zoneName)
    {
        return TryGetZoneInfo(zoneName, out ZoneInfo info) ? info.FillColor : DefaultZoneInfo.FillColor;
    }

    public IReadOnlyList<string> GetKnownCategories(bool includeUnspecified = false)
    {
        return ImdfUnitCategoryCatalog.GetCategories(includeLegacy: true, includeUnspecified: includeUnspecified);
    }

    private static IReadOnlyDictionary<string, ZoneInfo> ParseZoneLookup(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
        {
            return new Dictionary<string, ZoneInfo>(StringComparer.Ordinal);
        }

        Dictionary<string, ZoneInfo>? parsed = token.ToObject<Dictionary<string, ZoneInfo>>();
        return parsed ?? new Dictionary<string, ZoneInfo>(StringComparer.Ordinal);
    }

    private static void AddZoneAliases(
        IDictionary<string, ZoneInfo> target,
        ZoneInfo info,
        params string[] aliases)
    {
        ZoneInfo normalized = info.Normalized();
        for (int i = 0; i < aliases.Length; i++)
        {
            string key = NormalizeKey(aliases[i]);
            if (key.Length > 0)
            {
                target[key] = normalized;
            }
        }
    }

    private static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim().Replace('\u3000', ' ');
        StringBuilder builder = new(trimmed.Length);
        bool previousWasSpace = false;
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            bool isSpace = c == ' ';
            if (isSpace && previousWasSpace)
            {
                continue;
            }

            builder.Append(c);
            previousWasSpace = isSpace;
        }

        return builder.ToString();
    }

    private void RegisterCategoryInfo(ZoneInfo info)
    {
        string category = NormalizeKey(info.Category);
        if (category.Length == 0)
        {
            return;
        }

        if (!_categoryLookup.TryGetValue(category, out ZoneInfo? existing))
        {
            _categoryLookup[category] = info;
            return;
        }

        if (!string.IsNullOrWhiteSpace(existing.Restriction) && string.IsNullOrWhiteSpace(info.Restriction))
        {
            _categoryLookup[category] = info;
        }
    }

    private static IReadOnlyDictionary<string, ZoneInfo> EmptyFamilyLookup =>
        new Dictionary<string, ZoneInfo>(StringComparer.Ordinal);
}

public sealed class ZoneInfo
{
    public ZoneInfo(string category, string fillColor, string? restriction)
    {
        Category = category ?? string.Empty;
        FillColor = fillColor ?? ZoneCatalog.DefaultColor;
        Restriction = restriction;
    }

    public string Category { get; }

    public string FillColor { get; }

    public string? Restriction { get; }

    public ZoneInfo Normalized()
    {
        string category = string.IsNullOrWhiteSpace(Category) ? "unspecified" : Category.Trim();
        string fillColor = NormalizeColor(FillColor);
        string? restriction = Restriction;
        if (string.IsNullOrWhiteSpace(restriction))
        {
            restriction = null;
        }
        else
        {
            restriction = restriction!.Trim();
        }

        return new ZoneInfo(category, fillColor, restriction);
    }

    public static ZoneInfo Default()
    {
        return new ZoneInfo("unspecified", ZoneCatalog.DefaultColor, null);
    }

    public static ZoneInfo StairsDefault()
    {
        return new ZoneInfo("stairs", "C0C0C0", null);
    }

    private static string NormalizeColor(string color)
    {
        string normalized = color.Trim().TrimStart('#');
        return normalized.Length == 6 ? normalized.ToUpperInvariant() : ZoneCatalog.DefaultColor;
    }
}

