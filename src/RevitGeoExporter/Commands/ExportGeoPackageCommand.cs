using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Diagnostics;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
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
        IReadOnlyList<string> availableFloorTypeNames = GetAvailableFloorTypeNames(document);
        ModelCoordinateInfo coordinateInfo = new ModelCoordinateInfoReader().Read(document);

        ExportDialogResult? request = null;
        bool forceLegacyWinForms = string.Equals(
            Environment.GetEnvironmentVariable("REVIT_GEOEXPORTER_FORCE_LEGACY_WINFORMS_UI"),
            "1",
            StringComparison.Ordinal);

        bool useWpfDialog = !forceLegacyWinForms;
        bool useWpfPreviewWindow = false;

        if (useWpfDialog)
        {
            using ExportDialogWpf dialog = new(
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
                    using ProjectMappingsForm mappingsForm = new(
                        projectKey,
                        ZoneCatalog.CreateDefault(),
                        new MappingRuleStore(),
                        availableFloorTypeNames);
                    mappingsForm.ShowDialog();
                },
                (previewRequest, previewOwner) =>
                {
                    ExportPreviewService previewService = new(document, previewRequest.UnitSource, previewRequest.RoomCategoryParameterName, previewRequest.GeometryRepairOptions);
                    if (useWpfPreviewWindow)
                    {
                        using ExportPreviewWindow previewWindow = new(previewRequest, previewService);
                        previewWindow.ShowDialog();
                    }
                    else
                    {
                        using ExportPreviewForm previewForm = new(previewRequest, previewService);
                        if (previewOwner != null)
                        {
                            previewForm.ShowDialog(previewOwner);
                        }
                        else
                        {
                            previewForm.ShowDialog();
                        }
                    }
                },
                coordinateInfo);

            if (dialog.ShowDialog() != DialogResult.OK || dialog.Result == null)
            {
                return Result.Cancelled;
            }

            request = dialog.Result;
            bundle.SaveGlobalSettings(dialog.BuildSettings());
        }
        else
        {
            using ExportDialog dialog = new(
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
                       using ProjectMappingsForm mappingsForm = new(
                           projectKey,
                           ZoneCatalog.CreateDefault(),
                           new MappingRuleStore(),
                           availableFloorTypeNames);
                       mappingsForm.ShowDialog();
                   },
                   previewRequest =>
                   {
                       ExportPreviewService previewService = new(document, previewRequest.UnitSource, previewRequest.RoomCategoryParameterName, previewRequest.GeometryRepairOptions);
                       if (useWpfPreviewWindow)
                       {
                           using ExportPreviewWindow previewWindow = new(previewRequest, previewService);
                           previewWindow.ShowDialog();
                       }
                       else
                       {
                           using ExportPreviewForm previewForm = new(previewRequest, previewService);
                           previewForm.ShowDialog();
                       }
                   },
                   coordinateInfo);

            if (dialog.ShowDialog() != DialogResult.OK || dialog.Result == null)
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
            ExportValidationSnapshotBuilder snapshotBuilder = new();
            ExportValidationService validationService = new();

            PreparedExportSession session;
            ExportValidationResult validationResult;
            while (true)
            {
                session = exporter.PrepareExport(
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
                    BuildBaselineKey(projectKey, request.SelectedProfileName),
                    request.CoordinateMode,
                    coordinateInfo.ResolvedSourceEpsg,
                    coordinateInfo.SiteCoordinateSystemId,
                    coordinateInfo.SiteCoordinateSystemDefinition,
                    request.UnitSource,
                    request.RoomCategoryParameterName);

                ExportValidationRequest validationRequest = snapshotBuilder.Build(session);
                validationResult = validationService.Validate(validationRequest);

                using ExportValidationForm validationForm = new(
                    validationResult,
                    request.UiLanguage,
                    ValidationIssueResolutionForm.HasResolvableIssues(validationRequest));
                _ = validationForm.ShowDialog();

                if (validationForm.Outcome == ExportValidationOutcome.ResolveIssues)
                {
                    bool appliedChanges = TryResolveValidationIssues(document, projectKey, validationRequest, request.UiLanguage);
                    if (!appliedChanges)
                    {
                        continue;
                    }

                    continue;
                }

                if (validationForm.Outcome != ExportValidationOutcome.ContinueExport)
                {
                    return Result.Cancelled;
                }

                break;
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

    private static bool TryResolveValidationIssues(
        Document document,
        string projectKey,
        ExportValidationRequest validationRequest,
        UiLanguage language)
    {
        using ValidationIssueResolutionForm resolutionForm = new(validationRequest, language);
        if (resolutionForm.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return false;
        }

        IReadOnlyList<string> warnings = ApplyValidationResolutions(
            document,
            projectKey,
            resolutionForm.SelectedFloorAssignments,
            resolutionForm.SelectedElementIdsToRegenerate,
            validationRequest.UnitSource);
        ShowWarningsIfNeeded(warnings, language);
        return true;
    }

    private static IReadOnlyList<string> ApplyValidationResolutions(
        Document document,
        string projectKey,
        IReadOnlyDictionary<string, string> floorAssignments,
        IReadOnlyList<long> elementIdsToRegenerate,
        UnitSource unitSource)
    {
        List<string> warnings = new();

        if (unitSource == UnitSource.Rooms)
        {
            RoomCategoryOverrideStore roomCategoryOverrideStore = new();
            foreach (KeyValuePair<string, string> entry in floorAssignments)
            {
                roomCategoryOverrideStore.SetOverride(projectKey, entry.Key, entry.Value);
            }
        }
        else
        {
            FloorCategoryOverrideStore floorCategoryOverrideStore = new();
            foreach (KeyValuePair<string, string> entry in floorAssignments)
            {
                floorCategoryOverrideStore.SetOverride(projectKey, entry.Key, entry.Value);
            }
        }

        List<long> distinctElementIds = elementIdsToRegenerate
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        if (distinctElementIds.Count == 0)
        {
            return warnings;
        }

        SharedParameterManager parameterManager = new(document);
        using Transaction transaction = new(document, "IMDF Export - Resolve Validation Issues");
        transaction.Start();
        parameterManager.EnsureParameters(warnings);

        foreach (long sourceElementId in distinctElementIds)
        {
            ElementId elementId = new(sourceElementId);
            Element? element = document.GetElement(elementId);
            if (element == null)
            {
                warnings.Add($"Element {sourceElementId} could not be found when regenerating export IDs.");
                continue;
            }

            _ = parameterManager.RegenerateElementId(element, warnings);
        }

        transaction.Commit();
        return warnings;
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
                "Export failed."),
            MainContent = T(
                language,
                "The export could not be completed. You can save an error report as a text file.",
                "The export could not be completed. You can save an error report as a text file."),
            ExpandedContent = reportText,
            AllowCancellation = true,
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        dialog.AddCommandLink(
            TaskDialogCommandLinkId.CommandLink1,
            T(
                language,
                "Save Error Report",
                "Save Error Report"));

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
                "Save Export Error Report"),
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
                    ? $"鬩幢ｽ｢繝ｻ・ｧ郢晢ｽｻ繝ｻ・ｨ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｩ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｼ鬮ｫ・ｲ繝ｻ・ｰ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｰ郢晢ｽｻ繝ｻ・ｱ鬩幢ｽ｢繝ｻ・ｧ髯ｷ莉｣繝ｻ繝ｻ・ｽ繝ｻ・ｿ髫ｴ蜿門ｾ励・・ｽ繝ｻ・ｭ髯区ｻゑｽｽ・･郢晢ｽｻ繝ｻ・ｰ鬩搾ｽｵ繝ｻ・ｺ郢晢ｽｻ繝ｻ・ｾ鬩搾ｽｵ繝ｻ・ｺ髯ｷ莨夲ｽｽ・ｱ髫ｨ・ｳ郢晢ｽｻ繝ｻ・ｸ繝ｻ・ｲ驛｢譎｢・ｽ・ｻn{saveDialog.FileName}"
                    : $"Error report saved.\n{saveDialog.FileName}");
        }
        catch (Exception ex)
        {
            TaskDialog.Show(
                ProjectInfo.Name,
                language == UiLanguage.Japanese
                    ? $"鬩幢ｽ｢繝ｻ・ｧ郢晢ｽｻ繝ｻ・ｨ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｩ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｼ鬮ｫ・ｲ繝ｻ・ｰ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｰ郢晢ｽｻ繝ｻ・ｱ鬩搾ｽｵ繝ｻ・ｺ郢晢ｽｻ繝ｻ・ｮ鬮｣蜴・ｽｽ・ｫ髫ｴ蜿門ｾ励・・ｽ繝ｻ・ｭ髯区ｻゑｽｽ・･驕ｶ鬆托ｽ･・｢隴ｽ譁舌・繝ｻ・ｱ鬮ｫ・ｰ繝ｻ・ｨ髯ｷ莨夲ｽｽ・ｱ郢晢ｽｻ繝ｻ・ｰ鬩搾ｽｵ繝ｻ・ｺ郢晢ｽｻ繝ｻ・ｾ鬩搾ｽｵ繝ｻ・ｺ髯ｷ莨夲ｽｽ・ｱ髫ｨ・ｳ郢晢ｽｻ繝ｻ・ｸ繝ｻ・ｲ驛｢譎｢・ｽ・ｻn\n{ex.Message}"
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

    private static IReadOnlyList<string> GetAvailableFloorTypeNames(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .Select(type => type.Name?.Trim() ?? string.Empty)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ShowWarningsIfNeeded(IReadOnlyList<string> warnings, UiLanguage language)
    {
        if (warnings == null || warnings.Count == 0)
        {
            return;
        }

        string prefix = T(
            language,
            "Some issues were encountered while applying validation fixes.",
            "Some issues were encountered while applying validation fixes.");
        string message = $"{prefix}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, warnings)}";
        TaskDialog.Show(ProjectInfo.Name, message);
    }
}



