using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoExporter.Core;
using RevitGeoExporter.Export;
using RevitGeoExporter.UI;

namespace RevitGeoExporter.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class ExportGeoPackageCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (commandData is null)
        {
            throw new ArgumentNullException(nameof(commandData));
        }

        UIDocument? uiDocument = commandData.Application?.ActiveUIDocument;
        Document? document = uiDocument?.Document;
        if (document is null)
        {
            TaskDialog.Show(ProjectInfo.Name, "An active document is required.");
            return Result.Failed;
        }

        ViewCollector collector = new();
        IReadOnlyList<ViewPlan> views = collector.GetExportablePlanViews(document);
        if (views.Count == 0)
        {
            TaskDialog.Show(ProjectInfo.Name, "No exportable plan views were found.");
            return Result.Failed;
        }

        ExportDialogSettingsStore settingsStore = new();
        ExportDialogSettings settings = settingsStore.Load();

        ExportDialogResult? request = null;
        using (ExportDialog dialog = new(views, settings))
        {
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK || dialog.Result == null)
            {
                return Result.Cancelled;
            }

            request = dialog.Result;
            settingsStore.Save(dialog.BuildSettings());
        }

        if (request == null || request.SelectedViews.Count == 0)
        {
            return Result.Cancelled;
        }

        try
        {
            FloorGeoPackageExporter exporter = new(document);
            FloorGeoPackageExportResult result;
            using (ExportProgressForm progressForm = new())
            {
                progressForm.Show();
                System.Windows.Forms.Application.DoEvents();

                result = exporter.ExportSelectedViews(
                    request.OutputDirectory,
                    targetEpsg: request.TargetEpsg,
                    selectedViews: request.SelectedViews,
                    featureTypes: request.FeatureTypes,
                    splitUnitsByWalls: request.SplitUnitsByWalls,
                    progressCallback: update =>
                    {
                        progressForm.UpdateProgress(update);
                        System.Windows.Forms.Application.DoEvents();
                    });

                progressForm.Close();
            }

            using ExportResultForm resultForm = new(result, request.OutputDirectory, request.UiLanguage);
            resultForm.ShowDialog();
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            ShowExportFailureDialog(ex, request);
            return Result.Failed;
        }
    }

    private static void ShowExportFailureDialog(Exception exception, ExportDialogResult? request)
    {
        UiLanguage language = request?.UiLanguage ?? UiLanguage.English;
        string reportText = BuildFailureReport(exception);

        TaskDialog dialog = new(ProjectInfo.Name)
        {
            MainInstruction = T(
                language,
                "Export failed.",
                "\u30a8\u30af\u30b9\u30dd\u30fc\u30c8\u306b\u5931\u6557\u3057\u307e\u3057\u305f\u3002"),
            MainContent = T(
                language,
                "The export could not be completed. You can save an error report as a text file.",
                "\u30a8\u30af\u30b9\u30dd\u30fc\u30c8\u3092\u5b8c\u4e86\u3067\u304d\u307e\u305b\u3093\u3067\u3057\u305f\u3002\u30a8\u30e9\u30fc\u5831\u544a\u3092\u30c6\u30ad\u30b9\u30c8\u30d5\u30a1\u30a4\u30eb\u3068\u3057\u3066\u4fdd\u5b58\u3067\u304d\u307e\u3059\u3002"),
            ExpandedContent = reportText,
            AllowCancellation = true,
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        dialog.AddCommandLink(
            TaskDialogCommandLinkId.CommandLink1,
            T(
                language,
                "Save Error Report",
                "\u30a8\u30e9\u30fc\u5831\u544a\u3092\u4fdd\u5b58"));

        TaskDialogResult dialogResult = dialog.Show();
        if (dialogResult != TaskDialogResult.CommandLink1)
        {
            return;
        }

        SaveFailureReportToTextFile(reportText, request?.OutputDirectory, language);
    }

    private static void SaveFailureReportToTextFile(string reportText, string? preferredDirectory, UiLanguage language)
    {
        string initialDirectory = ResolveReportDirectory(preferredDirectory);
        string defaultFileName = $"RevitGeoExporter-ExportError-{DateTime.Now:yyyyMMdd-HHmmss}.txt";

        using System.Windows.Forms.SaveFileDialog saveDialog = new()
        {
            Title = T(
                language,
                "Save Export Error Report",
                "\u30a8\u30af\u30b9\u30dd\u30fc\u30c8\u30a8\u30e9\u30fc\u5831\u544a\u306e\u4fdd\u5b58"),
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = "txt",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = initialDirectory,
            FileName = defaultFileName,
        };

        if (saveDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK ||
            string.IsNullOrWhiteSpace(saveDialog.FileName))
        {
            return;
        }

        try
        {
            File.WriteAllText(
                saveDialog.FileName,
                reportText,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            TaskDialog.Show(
                ProjectInfo.Name,
                language == UiLanguage.Japanese
                    ? $"\u30a8\u30e9\u30fc\u5831\u544a\u3092\u4fdd\u5b58\u3057\u307e\u3057\u305f\u3002\n{saveDialog.FileName}"
                    : $"Error report saved.\n{saveDialog.FileName}");
        }
        catch (Exception ex)
        {
            TaskDialog.Show(
                ProjectInfo.Name,
                language == UiLanguage.Japanese
                    ? $"\u30a8\u30e9\u30fc\u5831\u544a\u306e\u4fdd\u5b58\u306b\u5931\u6557\u3057\u307e\u3057\u305f\u3002\n\n{ex.Message}"
                    : $"Failed to save error report.\n\n{ex.Message}");
        }
    }

    private static string ResolveReportDirectory(string? preferredDirectory)
    {
        string trimmedPreferredDirectory = preferredDirectory?.Trim() ?? string.Empty;
        if (trimmedPreferredDirectory.Length > 0 && Directory.Exists(trimmedPreferredDirectory))
        {
            return trimmedPreferredDirectory;
        }

        string documentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(documentsDirectory) ? documentsDirectory : Environment.CurrentDirectory;
    }

    private static string BuildFailureReport(Exception exception)
    {
        StringBuilder reportBuilder = new();
        reportBuilder.AppendLine("RevitGeoExporter Export Error Report");
        reportBuilder.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        reportBuilder.AppendLine();
        reportBuilder.AppendLine(exception.ToString());
        return reportBuilder.ToString();
    }

    private static string T(UiLanguage language, string english, string japanese)
    {
        return UiLanguageText.Select(language, english, japanese);
    }
}
