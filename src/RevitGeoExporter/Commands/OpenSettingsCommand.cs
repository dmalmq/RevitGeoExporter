using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoExporter.UI;

namespace RevitGeoExporter.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class OpenSettingsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            ExportDialogSettingsStore store = new();
            var settingsLoad = store.LoadWithDiagnostics();
            ExportDialogSettings settings = settingsLoad.Value;
            ShowWarningsIfNeeded(settingsLoad.Warnings, settings.UiLanguage);

            using SettingsDialog dialog = new(settings);
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return Result.Cancelled;
            }

            ExportDialogSettings updatedSettings = dialog.BuildSettings();
            store.Save(updatedSettings);
            TaskDialog.Show(
                ProjectInfo.Name,
                UiLanguageText.Select(updatedSettings.UiLanguage, "Settings saved.", "設定を保存しました。"));
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            TaskDialog.Show(ProjectInfo.Name, $"Unable to open settings.\n\n{ex}");
            return Result.Failed;
        }
    }

    private static void ShowWarningsIfNeeded(IReadOnlyList<string> warnings, UiLanguage language)
    {
        if (warnings == null || warnings.Count == 0)
        {
            return;
        }

        TaskDialog.Show(
            ProjectInfo.Name,
            UiLanguageText.Select(
                language,
                "Some saved application settings could not be loaded. Defaults were used where needed.",
                "保存済み設定の一部を読み込めなかったため、必要な項目には既定値を使用しました。") +
            "\n\n" +
            string.Join("\n", warnings));
    }
}
