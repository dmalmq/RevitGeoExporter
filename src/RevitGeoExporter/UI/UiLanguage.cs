namespace RevitGeoExporter.UI;

public enum UiLanguage
{
    English = 0,
    Japanese = 1,
}

public static class UiLanguageText
{
    public static string Select(UiLanguage language, string english, string japanese)
    {
        return language == UiLanguage.Japanese ? japanese : english;
    }

    public static string DisplayName(UiLanguage language)
    {
        return Select(language, "English", "日本語");
    }
}
