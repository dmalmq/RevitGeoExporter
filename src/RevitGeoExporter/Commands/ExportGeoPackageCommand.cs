using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Diagnostics;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Validation;
using RevitGeoExporter.Export;
using RevitGeoExporter.Resources;
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
            TaskDialog.Show(
                ProjectInfo.Name,
                LocalizedTextProvider.Get(UiLanguage.English, "Command.ActiveDocumentRequired", "An active document is required."));
            return Result.Failed;
        }

        ViewCollector collector = new();
        IReadOnlyList<ViewPlan> views = collector.GetExportablePlanViews(document);
        if (views.Count == 0)
        {
            TaskDialog.Show(
                ProjectInfo.Name,
                LocalizedTextProvider.Get(UiLanguage.English, "Command.NoExportableViews", "No exportable plan views were found."));
            return Result.Failed;
        }

        string projectKey = DocumentProjectKeyBuilder.Create(document);
        SettingsBundle bundle = new(projectKey);
        SettingsBundleSnapshot bundleSnapshot = bundle.Load();
        ExportDialogSettings settings = bundleSnapshot.GlobalSettings;
        ExportProfileStore profileStore = new();

        ExportDialogResult? request = null;
        using (ExportDialog dialog = new(
                   views,
                   settings,
                   bundleSnapshot.Profiles,
                   saveProfileRequested: (scope, name, profileSettings) =>
                   {
                       profileStore.SaveProfile(projectKey, ExportProfile.FromSettings(name, scope, profileSettings));
                   },
                   renameProfileRequested: (profile, newName) =>
                   {
                       profileStore.RenameProfile(projectKey, profile, newName);
                   },
                   deleteProfileRequested: profile =>
                   {
                       profileStore.DeleteProfile(projectKey, profile);
                   },
                   openMappingsRequested: () =>
                   {
                       using SettingsHubForm settingsHub = new(
                           projectKey,
                           bundle,
                           bundle.Load(),
                           ZoneCatalog.CreateDefault());
                       settingsHub.ShowDialog();
                   },
                       previewRequest =>
                   {
                       ExportPreviewService previewService = new(document, previewRequest.GeometryRepairOptions);
                       using ExportPreviewForm previewForm = new(previewRequest, previewService);
                       previewForm.ShowDialog();
                   }))
        {
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK || dialog.Result == null)
            {
                return Result.Cancelled;
            }

            request = dialog.Result;
            bundle.SaveGlobalSettings(dialog.BuildSettings());
        }

        if (request == null || request.SelectedViews.Count == 0)
        {
            return Result.Cancelled;
        }

        try
        {
            FloorGeoPackageExporter exporter = new(document);
            PreparedExportSession session = exporter.PrepareExport(
                request.OutputDirectory,
                request.TargetEpsg,
                request.SelectedViews,
                request.FeatureTypes,
                request.GeometryRepairOptions,
                new ExportPackageOptions
                {
                    Enabled = request.GeneratePackageOutput,
                    IncludeLegendFile = request.IncludePackageLegend,
                },
                request.SelectedProfileName,
                BuildBaselineKey(projectKey, request.SelectedProfileName));

            ExportValidationSnapshotBuilder snapshotBuilder = new();
            ExportValidationRequest validationRequest = snapshotBuilder.Build(session);
            ExportValidationService validationService = new();
            ExportValidationResult validationResult = validationService.Validate(validationRequest);

            using (ExportValidationForm validationForm = new(validationResult, request.UiLanguage))
            {
                if (validationForm.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return Result.Cancelled;
                }
            }

            FloorGeoPackageExportResult result;
            Stopwatch stopwatch = Stopwatch.StartNew();
            using (ExportProgressForm progressForm = new())
            {
                progressForm.Show();
                progressForm.Refresh();

                result = exporter.WritePreparedExport(
                    session,
                    progressCallback: update => { progressForm.UpdateProgress(update); });

                progressForm.Close();
            }

            stopwatch.Stop();

            ExportDiagnosticsReportBuilder diagnosticsBuilder = new();
            ExportDiagnosticsReport diagnosticsReport = diagnosticsBuilder.Build(
                session,
                validationResult,
                result,
                DateTimeOffset.UtcNow,
                stopwatch.Elapsed);

            if (request.GenerateDiagnosticsReport)
            {
                try
                {
                    ExportDiagnosticsWriter diagnosticsWriter = new();
                    string diagnosticsPath = diagnosticsWriter.WriteJson(request.OutputDirectory, diagnosticsReport);
                    result.SetDiagnosticsReportPath(diagnosticsPath);
                }
                catch (Exception diagnosticsException)
                {
                    result.AddWarnings(
                        new[]
                        {
                            $"Diagnostics report could not be written: {diagnosticsException.Message}",
                        });
                }
            }

            ExportPackageService packageService = new();
            ExportPackageResult packageResult = packageService.BuildPackage(session, diagnosticsReport, result);
            result.SetPackagePaths(packageResult.PackageDirectory, packageResult.ManifestPath);

            ExportBaselineStore baselineStore = new();
            var baseline = baselineStore.Load(session.BaselineKey);
            result.AddWarnings(baseline.Warnings);
            ChangeSummaryService changeSummaryService = new();
            result.SetChangeSummary(changeSummaryService.Compare(
                baseline.Report,
                diagnosticsReport,
                baseline.Manifest,
                packageResult.Manifest));
            baselineStore.Save(session.BaselineKey, diagnosticsReport, packageResult.Manifest);

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
                "エクスポートに失敗しました。"),
            MainContent = T(
                language,
                "The export could not be completed. You can save an error report as a text file.",
                "エクスポートを完了できませんでした。エラー報告をテキストファイルとして保存できます。"),
            ExpandedContent = reportText,
            AllowCancellation = true,
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        dialog.AddCommandLink(
            TaskDialogCommandLinkId.CommandLink1,
            T(
                language,
                "Save Error Report",
                "エラー報告を保存"));

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
                "エクスポートエラー報告の保存"),
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
                    ? $"エラー報告を保存しました。\n{saveDialog.FileName}"
                    : $"Error report saved.\n{saveDialog.FileName}");
        }
        catch (Exception ex)
        {
            TaskDialog.Show(
                ProjectInfo.Name,
                language == UiLanguage.Japanese
                    ? $"エラー報告の保存に失敗しました。\n\n{ex.Message}"
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

    private static string BuildBaselineKey(string projectKey, string? profileName)
    {
        return string.IsNullOrWhiteSpace(profileName)
            ? projectKey
            : $"{projectKey}__{profileName!.Trim()}";
    }

    private static void ShowWarningsIfNeeded(IReadOnlyList<string> warnings, UiLanguage language)
    {
        if (warnings == null || warnings.Count == 0)
        {
            return;
        }

        string prefix = T(
            language,
            "Some saved application settings could not be loaded. Defaults were used where needed.",
            "保存済み設定の一部を読み込めませんでした。必要に応じて既定値を使用しました。");
        string message = $"{prefix}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, warnings)}";
        TaskDialog.Show(ProjectInfo.Name, message);
    }
}
