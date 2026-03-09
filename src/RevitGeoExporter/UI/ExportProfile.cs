using System;

namespace RevitGeoExporter.UI;

public sealed class ExportProfile
{
    public string Name { get; set; } = string.Empty;

    public ExportProfileScope Scope { get; set; } = ExportProfileScope.Project;

    public string OutputDirectory { get; set; } = string.Empty;

    public int TargetEpsg { get; set; } = ProjectInfo.DefaultTargetEpsg;

    public RevitGeoExporter.Export.ExportFeatureType FeatureTypes { get; set; } =
        RevitGeoExporter.Export.ExportFeatureType.All;

    public bool GenerateDiagnosticsReport { get; set; } = true;

    public UiLanguage UiLanguage { get; set; } = UiLanguage.English;

    public ExportDialogSettings ToSettings()
    {
        return new ExportDialogSettings
        {
            OutputDirectory = OutputDirectory,
            TargetEpsg = TargetEpsg,
            FeatureTypes = FeatureTypes,
            GenerateDiagnosticsReport = GenerateDiagnosticsReport,
            UiLanguage = UiLanguage,
        };
    }

    public static ExportProfile FromSettings(string name, ExportProfileScope scope, ExportDialogSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        return new ExportProfile
        {
            Name = name?.Trim() ?? string.Empty,
            Scope = scope,
            OutputDirectory = settings.OutputDirectory,
            TargetEpsg = settings.TargetEpsg,
            FeatureTypes = settings.FeatureTypes,
            GenerateDiagnosticsReport = settings.GenerateDiagnosticsReport,
            UiLanguage = settings.UiLanguage,
        };
    }
}
