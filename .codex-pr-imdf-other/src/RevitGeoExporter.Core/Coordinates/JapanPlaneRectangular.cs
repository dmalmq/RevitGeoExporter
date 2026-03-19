using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RevitGeoExporter.Core.Coordinates;

public static class JapanPlaneRectangular
{
    private static readonly Regex EpsgRegex = new(@"EPSG\D*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex JgdZoneRegex = new(@"JGD\s*2011\D*0?([1-9]|1[0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ZoneRegex = new(@"(?:Zone|CS)\s*(\d{1,2}|XIX|XVIII|XVII|XVI|XV|XIV|XIII|XII|XI|X|IX|VIII|VII|VI|V|IV|III|II|I)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static readonly IReadOnlyList<JapanPlaneRectangularZoneDefinition> AllZones = new[]
    {
        new JapanPlaneRectangularZoneDefinition(6669, 1, "I", 33d, 129.5d),
        new JapanPlaneRectangularZoneDefinition(6670, 2, "II", 33d, 131d),
        new JapanPlaneRectangularZoneDefinition(6671, 3, "III", 36d, 132.16666666666666d),
        new JapanPlaneRectangularZoneDefinition(6672, 4, "IV", 33d, 133.5d),
        new JapanPlaneRectangularZoneDefinition(6673, 5, "V", 36d, 134.33333333333334d),
        new JapanPlaneRectangularZoneDefinition(6674, 6, "VI", 36d, 136d),
        new JapanPlaneRectangularZoneDefinition(6675, 7, "VII", 36d, 137.16666666666666d),
        new JapanPlaneRectangularZoneDefinition(6676, 8, "VIII", 36d, 138.5d),
        new JapanPlaneRectangularZoneDefinition(6677, 9, "IX", 36d, 139.83333333333334d),
        new JapanPlaneRectangularZoneDefinition(6678, 10, "X", 40d, 140.83333333333334d),
        new JapanPlaneRectangularZoneDefinition(6679, 11, "XI", 44d, 140.25d),
        new JapanPlaneRectangularZoneDefinition(6680, 12, "XII", 44d, 142.25d),
        new JapanPlaneRectangularZoneDefinition(6681, 13, "XIII", 44d, 144.25d),
        new JapanPlaneRectangularZoneDefinition(6682, 14, "XIV", 26d, 142d),
        new JapanPlaneRectangularZoneDefinition(6683, 15, "XV", 26d, 127.5d),
        new JapanPlaneRectangularZoneDefinition(6684, 16, "XVI", 26d, 124d),
        new JapanPlaneRectangularZoneDefinition(6685, 17, "XVII", 26d, 131d),
        new JapanPlaneRectangularZoneDefinition(6686, 18, "XVIII", 20d, 136d),
        new JapanPlaneRectangularZoneDefinition(6687, 19, "XIX", 26d, 154d),
    };

    public static readonly IReadOnlyDictionary<int, string> Zones = AllZones
        .ToDictionary(zone => zone.Epsg, zone => zone.DisplayName);

    public static bool TryGetZone(int epsg, out JapanPlaneRectangularZoneDefinition? zone)
    {
        zone = AllZones.FirstOrDefault(candidate => candidate.Epsg == epsg);
        return zone != null;
    }

    public static string DescribeEpsg(int epsg)
    {
        if (TryGetZone(epsg, out JapanPlaneRectangularZoneDefinition? zone) && zone != null)
        {
            return $"EPSG:{zone.Epsg} - {zone.DisplayName}";
        }

        return $"EPSG:{epsg}";
    }

    public static bool TryResolveEpsg(string? text, out int epsg)
    {
        string normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            epsg = 0;
            return false;
        }

        Match epsgMatch = EpsgRegex.Match(normalized);
        if (epsgMatch.Success && int.TryParse(epsgMatch.Groups[1].Value, out epsg))
        {
            return epsg > 0;
        }

        Match jgdZoneMatch = JgdZoneRegex.Match(normalized);
        if (jgdZoneMatch.Success &&
            int.TryParse(jgdZoneMatch.Groups[1].Value, out int zoneNumber) &&
            TryResolveZoneEpsg(zoneNumber, out epsg))
        {
            return true;
        }

        bool mentionsJapanPlaneRectangular =
            normalized.IndexOf("Japan Plane Rectangular", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalized.IndexOf("JGD2011", StringComparison.OrdinalIgnoreCase) >= 0;
        Match zoneMatch = ZoneRegex.Match(normalized);
        if (mentionsJapanPlaneRectangular && zoneMatch.Success)
        {
            string token = zoneMatch.Groups[1].Value.Trim();
            if (TryParseZoneNumber(token, out int parsedZoneNumber) &&
                TryResolveZoneEpsg(parsedZoneNumber, out epsg))
            {
                return true;
            }
        }

        epsg = 0;
        return false;
    }

    private static bool TryResolveZoneEpsg(int zoneNumber, out int epsg)
    {
        if (zoneNumber >= 1 && zoneNumber <= 19)
        {
            epsg = 6668 + zoneNumber;
            return true;
        }

        epsg = 0;
        return false;
    }

    private static bool TryParseZoneNumber(string token, out int zoneNumber)
    {
        if (int.TryParse(token, out zoneNumber))
        {
            return zoneNumber >= 1 && zoneNumber <= 19;
        }

        switch (token.ToUpperInvariant())
        {
            case "I": zoneNumber = 1; return true;
            case "II": zoneNumber = 2; return true;
            case "III": zoneNumber = 3; return true;
            case "IV": zoneNumber = 4; return true;
            case "V": zoneNumber = 5; return true;
            case "VI": zoneNumber = 6; return true;
            case "VII": zoneNumber = 7; return true;
            case "VIII": zoneNumber = 8; return true;
            case "IX": zoneNumber = 9; return true;
            case "X": zoneNumber = 10; return true;
            case "XI": zoneNumber = 11; return true;
            case "XII": zoneNumber = 12; return true;
            case "XIII": zoneNumber = 13; return true;
            case "XIV": zoneNumber = 14; return true;
            case "XV": zoneNumber = 15; return true;
            case "XVI": zoneNumber = 16; return true;
            case "XVII": zoneNumber = 17; return true;
            case "XVIII": zoneNumber = 18; return true;
            case "XIX": zoneNumber = 19; return true;
            default:
                zoneNumber = 0;
                return false;
        }
    }
}

public sealed class JapanPlaneRectangularZoneDefinition
{
    public JapanPlaneRectangularZoneDefinition(
        int epsg,
        int zoneNumber,
        string romanZone,
        double latitudeOfOriginDegrees,
        double centralMeridianDegrees)
    {
        Epsg = epsg;
        ZoneNumber = zoneNumber;
        RomanZone = romanZone;
        LatitudeOfOriginDegrees = latitudeOfOriginDegrees;
        CentralMeridianDegrees = centralMeridianDegrees;
    }

    public int Epsg { get; }

    public int ZoneNumber { get; }

    public string RomanZone { get; }

    public double LatitudeOfOriginDegrees { get; }

    public double CentralMeridianDegrees { get; }

    public string DisplayName => $"JGD2011-{ZoneNumber:00} / Zone {RomanZone}";
}