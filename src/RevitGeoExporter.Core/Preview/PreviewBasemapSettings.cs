using System;

namespace RevitGeoExporter.Core.Preview;

public sealed class PreviewBasemapSettings
{
    public const string DefaultUrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public const string DefaultAttribution = "\u00A9 OpenStreetMap contributors";

    public PreviewBasemapSettings(string? urlTemplate, string? attribution)
    {
        UrlTemplate = urlTemplate == null ? DefaultUrlTemplate : NormalizeUrlTemplate(urlTemplate);
        Attribution = attribution == null ? DefaultAttribution : attribution.Trim();
    }

    public string UrlTemplate { get; }

    public string Attribution { get; }

    public bool IsConfigured => UrlTemplate.Length > 0;

    private static string NormalizeUrlTemplate(string urlTemplate)
    {
        string trimmed = urlTemplate.Trim();
        return trimmed.Equals("offline", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : trimmed;
    }
}
