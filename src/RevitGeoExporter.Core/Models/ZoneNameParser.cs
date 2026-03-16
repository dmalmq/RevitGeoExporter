using System;

namespace RevitGeoExporter.Core.Models;

public static class ZoneNameParser
{
    public const string DefaultPrefix = "j ";
    public const string DefaultSuffix = "_床";

    public static ZoneNameParseResult Parse(
        string typeName,
        string prefix = DefaultPrefix,
        string suffix = DefaultSuffix)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            throw new ArgumentException("Type name is required.", nameof(typeName));
        }

        string trimmed = typeName.Trim();
        bool prefixMatched = !string.IsNullOrEmpty(prefix) &&
                             trimmed.StartsWith(prefix, StringComparison.Ordinal);
        bool suffixMatched = !string.IsNullOrEmpty(suffix) &&
                             trimmed.EndsWith(suffix, StringComparison.Ordinal);

        if (!(prefixMatched && suffixMatched))
        {
            return new ZoneNameParseResult(trimmed, prefixMatched, suffixMatched);
        }

        int start = prefix.Length;
        int length = trimmed.Length - prefix.Length - suffix.Length;
        if (length <= 0)
        {
            return new ZoneNameParseResult(trimmed, prefixMatched, suffixMatched);
        }

        string zoneName = trimmed.Substring(start, length);
        return new ZoneNameParseResult(zoneName, prefixMatched, suffixMatched);
    }
}

public readonly struct ZoneNameParseResult
{
    public ZoneNameParseResult(string zoneName, bool prefixMatched, bool suffixMatched)
    {
        ZoneName = zoneName;
        PrefixMatched = prefixMatched;
        SuffixMatched = suffixMatched;
    }

    public string ZoneName { get; }

    public bool PrefixMatched { get; }

    public bool SuffixMatched { get; }

    public bool PatternMatched => PrefixMatched && SuffixMatched;
}
