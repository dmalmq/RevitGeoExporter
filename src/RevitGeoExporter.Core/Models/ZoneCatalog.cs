using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitGeoExporter.Core.Models;

public sealed class ZoneCatalog
{
    public const string DefaultColor = "CCCCCC";

    private readonly Dictionary<string, ZoneInfo> _zoneLookup;
    private readonly Dictionary<string, ZoneInfo> _familyLookup;

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
        foreach (KeyValuePair<string, ZoneInfo> entry in zoneLookup)
        {
            string zoneName = NormalizeKey(entry.Key);
            if (zoneName.Length == 0 || entry.Value is null)
            {
                continue;
            }

            _zoneLookup[zoneName] = entry.Value.Normalized();
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
            "繝ｩ繝∝､悶さ繝ｳ繧ｳ繝ｼ繧ｹ",
            "Circulation (outside fare gates)");

        AddZoneAliases(
            zones,
            new ZoneInfo("walkway", "FEFEF2", "rachi_nai"),
            "ラチ内コンコース",
            "繝ｩ繝∝・繧ｳ繝ｳ繧ｳ繝ｼ繧ｹ",
            "Circulation (inside fare gates)");

        AddZoneAliases(
            zones,
            new ZoneInfo("walkway", "EFFAED", "rachi_nai"),
            "ラチ内コンコース(JR東日本新幹線)",
            "繝ｩ繝∝・繧ｳ繝ｳ繧ｳ繝ｼ繧ｹ(JR譚ｱ譁ｰ蟷ｹ邱・",
            "Circulation (JR East Shinkansen)");

        AddZoneAliases(
            zones,
            new ZoneInfo("walkway", "FFF4E0", "rachi_nai"),
            "ラチ内コンコース(JR東海新幹線)",
            "繝ｩ繝∝・繧ｳ繝ｳ繧ｳ繝ｼ繧ｹ(JR譚ｱ豬ｷ譁ｰ蟷ｹ邱・",
            "Circulation (JR Central Shinkansen)");

        AddZoneAliases(
            zones,
            new ZoneInfo("retail", "E1F3F9", "rachi_nai"),
            "ラチ内店舗",
            "繝ｩ繝∝・蠎苓・",
            "Retail (inside fare gates)");

        AddZoneAliases(
            zones,
            new ZoneInfo("retail", "F3FCFF", "rachi_gai"),
            "ラチ外店舗",
            "繝ｩ繝∝､門ｺ苓・",
            "Retail (outside fare gates)");

        AddZoneAliases(
            zones,
            new ZoneInfo("platform", "F9E8E1", "rachi_nai"),
            "新幹線ホーム",
            "譁ｰ蟷ｹ邱壹・繝ｼ繝",
            "Platform (Shinkansen)");

        AddZoneAliases(
            zones,
            new ZoneInfo("platform", "FFF6F3", "rachi_nai"),
            "在来線ホーム",
            "蝨ｨ譚･邱壹・繝ｼ繝",
            "Platform (conventional lines)");

        AddZoneAliases(
            zones,
            new ZoneInfo("unspecified", "F3F3F3", null),
            "その他",
            "縺昴・莉・",
            "Other / miscellaneous");

        AddZoneAliases(
            zones,
            new ZoneInfo("information", "EFEFF9", null),
            "案内所",
            "譯亥・謇",
            "Information desk");

        AddZoneAliases(
            zones,
            new ZoneInfo("ticketing", "A3CA7F", "rachi_gai"),
            "みどりの窓口",
            "縺ｿ縺ｩ繧翫・遯灘哨",
            "Ticket office (Midori no Madoguchi)");

        AddZoneAliases(
            zones,
            new ZoneInfo("road", "E4E5E5", null),
            "道路",
            "驕楢ｷｯ",
            "Road");

        AddZoneAliases(
            zones,
            new ZoneInfo("outdoors", "FFFFFF", null),
            "外構",
            "螟匁ｧ・",
            "Roof",
            "屋根",
            "Exterior / grounds");

        AddZoneAliases(
            zones,
            new ZoneInfo("restroom.male", "BBD2EF", null),
            "男子トイレ",
            "逕ｷ蟄舌ヨ繧､繝ｬ",
            "Men's restroom");

        AddZoneAliases(
            zones,
            new ZoneInfo("restroom.female", "FFA4A4", null),
            "女子トイレ",
            "螂ｳ蟄舌ヨ繧､繝ｬ",
            "Women's restroom");

        AddZoneAliases(
            zones,
            new ZoneInfo("restroom.unisex", "D9FFD9", null),
            "多目的トイレ",
            "螟夂岼逧・ヨ繧､繝ｬ",
            "Accessible / multipurpose restroom");

        AddZoneAliases(
            zones,
            new ZoneInfo("ticketing", "C2E389", null),
            "券売機スペース",
            "蛻ｸ螢ｲ讖溷ｮ､",
            "Ticket machine area");

        AddZoneAliases(
            zones,
            new ZoneInfo("waitingroom", "BABABA", null),
            "待合室",
            "蠕・粋螳､",
            "Waiting room");

        AddZoneAliases(
            zones,
            new ZoneInfo("retail", "979797", "rachi_gai"),
            "物販・飲食（駅外商業施設）",
            "蠎苓・・井ｻ門膚讌ｭ譁ｽ險ｭ・・",
            "Retail (external commercial facilities)");

        Dictionary<string, ZoneInfo> families = new(StringComparer.Ordinal)
        {
            ["j EV"] = new ZoneInfo("elevator", "E0E0E0", null),
            ["j エスカレーター-lightweight"] = new ZoneInfo("escalator", "D0D0D0", null),
            ["j 繧ｨ繧ｹ繧ｫ繝ｬ繝ｼ繧ｿ-lightweight"] = new ZoneInfo("escalator", "D0D0D0", null),
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
