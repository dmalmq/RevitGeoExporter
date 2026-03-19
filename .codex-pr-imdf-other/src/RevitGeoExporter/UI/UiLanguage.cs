using System.ComponentModel;
using System.Globalization;
using RevitGeoExporter.Resources;

namespace RevitGeoExporter.UI;

public enum UiLanguage
{
    English = 0,
    Japanese = 1,
}

public static class UiLanguageText
{
    public static string Get(UiLanguage language, string key, string fallback)
    {
        return LocalizedTextProvider.Get(language, key, fallback);
    }

    public static string Format(UiLanguage language, string key, string fallbackFormat, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(language, key, fallbackFormat), args);
    }

    public static string DisplayName(UiLanguage language)
    {
        return language == UiLanguage.Japanese
            ? LocalizedTextProvider.Get(language, "Language.Japanese", "Japanese")
            : LocalizedTextProvider.Get(language, "Language.English", "English");
    }

    // Temporary compatibility shim for callers that still provide inline bilingual text.
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static string Select(UiLanguage language, string english, string japanese)
    {
        return language == UiLanguage.Japanese ? japanese : english;
    }
}
