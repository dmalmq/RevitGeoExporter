using System;

namespace RevitGeoExporter.Core.Preview;

public sealed class PreviewBasemapSettings
{
    public const string DefaultUrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public const string DefaultAttribution = "\u00A9 OpenStreetMap contributors";

    public PreviewBasemapSettings(string? urlTemplate, string? attribution)
    {
        UrlTemplate = string.IsNullOrWhiteSpace(urlTemplate) ? DefaultUrlTemplate : urlTemplate.Trim();
        Attribution = string.IsNullOrWhiteSpace(attribution) ? DefaultAttribution : attribution.Trim();
    }

    public string UrlTemplate { get; }

    public string Attribution { get; }

    public bool IsConfigured => UrlTemplate.Length > 0;
}
