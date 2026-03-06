using System;
using System.Collections.Generic;
using RevitGeoExporter.Core.Models;

namespace RevitGeoExporter.Core.Preview;

public sealed class PreviewPaletteResolver
{
    private readonly Dictionary<string, string> _overrides;

    public PreviewPaletteResolver(IReadOnlyDictionary<string, string>? overrides = null)
    {
        _overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> entry in overrides ?? CreateDefaultOverrides())
        {
            string category = (entry.Key ?? string.Empty).Trim();
            if (category.Length == 0)
            {
                continue;
            }

            _overrides[category] = NormalizeHex(entry.Value, ZoneCatalog.DefaultColor);
        }
    }

    public string ResolveFillColor(string? category, string? fallbackHex = null)
    {
        string trimmedCategory = (category ?? string.Empty).Trim();
        if (trimmedCategory.Length > 0 &&
            _overrides.TryGetValue(trimmedCategory, out string resolved))
        {
            return resolved;
        }

        return NormalizeHex(fallbackHex, ZoneCatalog.DefaultColor);
    }

    private static IReadOnlyDictionary<string, string> CreateDefaultOverrides()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["retail"] = "F3FCFF",
            ["walkway"] = "FEFEF2",
            ["nonpublic"] = "979797",
            ["restroom.male"] = "BBD2EF",
            ["restroom.female"] = "FFA4A4",
        };
    }

    private static string NormalizeHex(string? hex, string fallbackHex)
    {
        string candidate = (hex ?? string.Empty).Trim().TrimStart('#');
        if (candidate.Length != 6)
        {
            return fallbackHex;
        }

        for (int i = 0; i < candidate.Length; i++)
        {
            if (!Uri.IsHexDigit(candidate[i]))
            {
                return fallbackHex;
            }
        }

        return candidate.ToUpperInvariant();
    }
}
