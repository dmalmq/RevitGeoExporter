using System.Collections.Generic;
using System.Linq;

namespace RevitGeoExporter.Core.Coordinates;

public sealed class CrsPresetEntry
{
    public CrsPresetEntry(int epsg, string displayName)
    {
        Epsg = epsg;
        DisplayName = displayName;
    }

    public int Epsg { get; }

    public string DisplayName { get; }
}

public sealed class CrsPresetGroup
{
    public CrsPresetGroup(string region, IReadOnlyList<CrsPresetEntry> entries)
    {
        Region = region;
        Entries = entries;
    }

    public string Region { get; }

    public IReadOnlyList<CrsPresetEntry> Entries { get; }
}

public static class CrsPresetCatalog
{
    private static IReadOnlyList<CrsPresetGroup>? _groups;

    public static IReadOnlyList<CrsPresetGroup> GetAllGroups()
    {
        return _groups ??= BuildGroups();
    }

    public static string? TryGetDisplayName(int epsg)
    {
        foreach (CrsPresetGroup group in GetAllGroups())
        {
            CrsPresetEntry? entry = group.Entries.FirstOrDefault(e => e.Epsg == epsg);
            if (entry != null)
            {
                return entry.DisplayName;
            }
        }

        return null;
    }

    private static IReadOnlyList<CrsPresetGroup> BuildGroups()
    {
        List<CrsPresetGroup> groups = new()
        {
            new CrsPresetGroup("Global", new[]
            {
                new CrsPresetEntry(4326, "WGS 84"),
                new CrsPresetEntry(3857, "WGS 84 / Pseudo-Mercator"),
            }),
            new CrsPresetGroup("Japan", BuildJapanPresets()),
            new CrsPresetGroup("Europe", new[]
            {
                new CrsPresetEntry(25832, "ETRS89 / UTM zone 32N"),
                new CrsPresetEntry(25833, "ETRS89 / UTM zone 33N"),
                new CrsPresetEntry(25834, "ETRS89 / UTM zone 34N"),
                new CrsPresetEntry(25835, "ETRS89 / UTM zone 35N"),
                new CrsPresetEntry(27700, "OSGB 1936 / British National Grid"),
                new CrsPresetEntry(2154, "RGF93 / Lambert-93 (France)"),
                new CrsPresetEntry(3035, "ETRS89 / LAEA Europe"),
                new CrsPresetEntry(32632, "WGS 84 / UTM zone 32N"),
                new CrsPresetEntry(32633, "WGS 84 / UTM zone 33N"),
            }),
            new CrsPresetGroup("North America", new[]
            {
                new CrsPresetEntry(26917, "NAD83 / UTM zone 17N"),
                new CrsPresetEntry(26918, "NAD83 / UTM zone 18N"),
                new CrsPresetEntry(26919, "NAD83 / UTM zone 19N"),
                new CrsPresetEntry(32617, "WGS 84 / UTM zone 17N"),
                new CrsPresetEntry(32618, "WGS 84 / UTM zone 18N"),
                new CrsPresetEntry(2263, "NAD83 / New York Long Island (ftUS)"),
                new CrsPresetEntry(2227, "NAD83 / California zone 3 (ftUS)"),
            }),
            new CrsPresetGroup("Asia-Pacific", new[]
            {
                new CrsPresetEntry(7855, "GDA2020 / MGA zone 55"),
                new CrsPresetEntry(7856, "GDA2020 / MGA zone 56"),
                new CrsPresetEntry(2193, "NZGD2000 / New Zealand Transverse Mercator"),
                new CrsPresetEntry(32648, "WGS 84 / UTM zone 48N"),
                new CrsPresetEntry(32649, "WGS 84 / UTM zone 49N"),
                new CrsPresetEntry(32650, "WGS 84 / UTM zone 50N"),
                new CrsPresetEntry(32651, "WGS 84 / UTM zone 51N"),
            }),
        };

        return groups;
    }

    private static CrsPresetEntry[] BuildJapanPresets()
    {
        return JapanPlaneRectangular.AllZones
            .Select(zone => new CrsPresetEntry(zone.Epsg, zone.DisplayName))
            .ToArray();
    }
}
