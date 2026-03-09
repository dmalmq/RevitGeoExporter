using System.Collections.Generic;
using RevitGeoExporter.Resources;

namespace RevitGeoExporter.UI;

public enum UiLanguage
{
    English = 0,
    Japanese = 1,
}

public static class UiLanguageText
{
    private static readonly IReadOnlyDictionary<string, string> KnownEnglishKeys = new Dictionary<string, string>
    {
        ["Export GeoPackage"] = "ExportDialog.Title",
        ["Plan Views"] = "ExportDialog.PlanViews",
        ["Export Options"] = "ExportDialog.Options",
        ["Language"] = "Common.Language",
        ["Feature Types"] = "ExportDialog.FeatureTypes",
        ["Output Directory"] = "Common.OutputDirectory",
        ["Browse..."] = "Common.Browse",
        ["Cancel"] = "Common.Cancel",
        ["Preview..."] = "ExportDialog.Preview",
        ["Export"] = "ExportDialog.ExportButton",
        ["Write diagnostics report"] = "ExportDialog.WriteDiagnostics",
        ["Write GIS package"] = "ExportDialog.WritePackage",
        ["Include legend file"] = "ExportDialog.IncludeLegend",
        ["Select All"] = "ExportDialog.SelectAll",
        ["Clear All"] = "ExportDialog.ClearAll",
        ["Export Profiles"] = "ExportDialog.ExportProfiles",
        ["Settings"] = "Ribbon.Settings.Text",
        ["Close"] = "Common.Close",
        ["Save"] = "Common.Save",
        ["Validation Results"] = "Validation.Title",
        ["No validation issues were found."] = "Validation.NoIssues",
        ["Continue Export"] = "Validation.Continue",
        ["Resolve Errors"] = "Validation.ResolveErrors",
        ["Export Results"] = "Result.Title",
        ["Export Preview"] = "Preview.Title",
        ["Units"] = "Preview.Units",
        ["Openings"] = "Preview.Openings",
        ["Details"] = "Preview.Details",
        ["Levels"] = "Preview.Levels",
        ["Stairs"] = "Preview.Stairs",
        ["Escalators"] = "Preview.Escalators",
        ["Elevators"] = "Preview.Elevators",
        ["Warnings"] = "Preview.Warnings",
        ["Overrides"] = "Preview.Overrides",
        ["Unassigned"] = "Preview.Unassigned",
        ["Fit"] = "Preview.Fit",
        ["Reset"] = "Preview.Reset",
        ["Select at least one plan view to export."] = "ExportDialog.Message.SelectPlanViewToExport",
        ["Select at least one feature type."] = "ExportDialog.Message.SelectFeatureType",
        ["Choose an output directory."] = "ExportDialog.Message.ChooseOutputDirectory",
        ["Enter a valid EPSG code."] = "ExportDialog.Message.EnterValidEpsg",
        ["Select at least one plan view to preview."] = "ExportDialog.Message.SelectPlanViewToPreview",
        ["Preview requires at least one selected feature type."] = "ExportDialog.Message.PreviewRequiresFeatureType",
        ["<no level>"] = "Common.NoLevel",
    };

    public static string Select(UiLanguage language, string english, string japanese)
    {
        if (language != UiLanguage.Japanese)
        {
            return ResolveLocalizedText(UiLanguage.English, english, english);
        }

        if (ContainsMojibake(japanese))
        {
            return ResolveLocalizedText(language, english, english);
        }

        return ResolveLocalizedText(language, english, japanese);
    }

    public static string DisplayName(UiLanguage language)
    {
        return language == UiLanguage.Japanese
            ? LocalizedTextProvider.Get(language, "Language.Japanese", "Japanese")
            : LocalizedTextProvider.Get(language, "Language.English", "English");
    }

    private static string ResolveLocalizedText(UiLanguage language, string english, string fallback)
    {
        if (KnownEnglishKeys.TryGetValue(english, out string? key))
        {
            return LocalizedTextProvider.Get(language, key, fallback);
        }

        return fallback;
    }

    private static bool ContainsMojibake(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               (value.IndexOf('繧') >= 0 || value.IndexOf('縺') >= 0 || value.IndexOf('譛') >= 0);
    }
}
