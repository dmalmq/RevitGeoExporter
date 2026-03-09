using System.Globalization;
using RevitGeoExporter.UI;

namespace RevitGeoExporter.Resources;

public static class LocalizedTextProvider
{
    public static string Get(UiLanguage language, string key, string? fallback = null)
    {
        CultureInfo culture = language == UiLanguage.Japanese
            ? CultureInfo.GetCultureInfo("ja")
            : CultureInfo.GetCultureInfo("en");
        string? localized = AppStrings.ResourceManager.GetString(key, culture);
        if (!string.IsNullOrWhiteSpace(localized))
        {
            return localized!;
        }

        string? neutral = AppStrings.ResourceManager.GetString(key, CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(neutral))
        {
            return neutral!;
        }

        return fallback ?? key;
    }
}
