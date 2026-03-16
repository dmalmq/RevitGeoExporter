using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Diagnostics;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Validation;
using RevitGeoExporter.Export;
using RevitGeoExporter.Resources;
using RevitGeoExporter.UI;
using WinForms = System.Windows.Forms;

namespace RevitGeoExporter.Commands;

internal sealed class ExportWorkflowCoordinator
{
    private readonly Document _document;
    private readonly UIDocument? _uiDocument;
    private readonly string _projectKey;
    private readonly IReadOnlyList<string> _availableFloorTypeNames;
    private readonly ExportProfileStore _profileStore;
    private readonly bool _useWpfPreviewWindow;

    public ExportWorkflowCoordinator(
        Document document,
        UIDocument? uiDocument,
        string projectKey,
        IReadOnlyList<string> availableFloorTypeNames,
        ExportProfileStore profileStore,
        bool useWpfPreviewWindow)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _uiDocument = uiDocument;
        _projectKey = string.IsNullOrWhiteSpace(projectKey)
            ? throw new ArgumentException("A project key is required.", nameof(projectKey))
            : projectKey.Trim();
        _availableFloorTypeNames = availableFloorTypeNames ?? throw new ArgumentNullException(nameof(availableFloorTypeNames));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _useWpfPreviewWindow = useWpfPreviewWindow;
    }

    public void SaveProfile(ExportProfileScope scope, string name, ExportDialogSettings profileSettings)
    {
        _profileStore.SaveProfile(_projectKey, ExportProfile.FromSettings(name, scope, profileSettings));
    }

    public void RenameProfile(ExportProfile profile, string newName)
    {
        _profileStore.RenameProfile(_projectKey, profile, newName);
    }

    public void DeleteProfile(ExportProfile profile)
    {
        _profileStore.DeleteProfile(_projectKey, profile);
    }

    public void OpenMappings()
    {
        using ProjectMappingsForm mappingsForm = new(
            _projectKey,
            ZoneCatalog.CreateDefault(),
            new MappingRuleStore(),
            _availableFloorTypeNames);
        mappingsForm.ShowDialog();
    }

    public void ShowPreview(ExportPreviewRequest previewRequest, WinForms.IWin32Window? owner = null)
    {
        ExportPreviewService previewService = new(
            _document,
            previewRequest.UnitSource,
            previewRequest.RoomCategoryParameterName,
            previewRequest.GeometryRepairOptions);

        if (_useWpfPreviewWindow)
        {
            using ExportPreviewWindow previewWindow = new(previewRequest, previewService, owner);
            _ = previewWindow.ShowDialog();
            return;
        }

        using ExportPreviewForm previewForm = new(previewRequest, previewService);
        if (owner != null)
        {
            previewForm.ShowDialog(owner);
        }
        else
        {
            previewForm.ShowDialog();
        }
    }

    public Result RunExport(ExportDialogResult request, ModelCoordinateInfo coordinateInfo, ref string message)
    {
        try
        {
            FloorGeoPackageExporter exporter = new(_document);
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
                    BuildBaselineKey(_projectKey, request.SelectedProfileName),
                    request.CoordinateMode,
                    coordinateInfo.ResolvedSourceEpsg,
                    coordinateInfo.SiteCoordinateSystemId,
                    coordinateInfo.SiteCoordinateSystemDefinition,
                    request.UnitSource,
                    request.RoomCategoryParameterName);

                ExportValidationRequest validationRequest = snapshotBuilder.Build(session);
                validationResult = validationService.Validate(validationRequest);
                SharedCoordinateValidationResult coordinateValidation = new SharedCoordinateValidator().Validate(_document);
                ExportReadinessSummary readinessSummary = new ExportReadinessSummaryBuilder().Build(
                    validationRequest,
                    validationResult,
                    ZoneCatalog.CreateDefault());
                bool canResolveIssues = ValidationIssueResolutionForm.HasResolvableIssues(validationRequest);

                using ExportReadinessForm readinessForm = new(
                    readinessSummary,
                    coordinateValidation,
                    request.UiLanguage,
                    canResolveIssues);
                _ = readinessForm.ShowDialog();

                if (readinessForm.Outcome == ExportReadinessOutcome.ResolveIssues)
                {
                    if (!TryResolveValidationIssues(validationRequest, request.UiLanguage))
                    {
                        continue;
                    }

                    continue;
                }

                if (readinessForm.Outcome == ExportReadinessOutcome.OpenMappings)
                {
                    OpenMappings();
                    continue;
                }

                if (readinessForm.Outcome != ExportReadinessOutcome.ContinueToValidation)
                {
                    return Result.Cancelled;
                }

                using ExportValidationForm validationForm = new(
                    validationResult,
                    request.UiLanguage,
                    canResolveIssues,
                    NavigateToValidationIssue);
                _ = validationForm.ShowDialog();

                if (validationForm.Outcome == ExportValidationOutcome.ResolveIssues)
                {
                    bool appliedChanges = TryResolveValidationIssues(validationRequest, request.UiLanguage);
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
                    progressCallback: update => progressForm.UpdateProgress(update));

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
            _ = resultForm.ShowDialog();
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            ShowExportFailureDialog(ex, request);
            return Result.Failed;
        }
    }

    private bool TryResolveValidationIssues(ExportValidationRequest validationRequest, UiLanguage language)
    {
        using ValidationIssueResolutionForm resolutionForm = new(validationRequest, language);
        if (resolutionForm.ShowDialog() != DialogResult.OK)
        {
            return false;
        }

        IReadOnlyList<string> warnings = ApplyValidationResolutions(
            resolutionForm.SelectedFloorAssignments,
            resolutionForm.SelectedElementIdsToRegenerate,
            validationRequest.UnitSource);
        ShowWarningsIfNeeded(warnings, language);
        return true;
    }

    private IReadOnlyList<string> ApplyValidationResolutions(
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
                roomCategoryOverrideStore.SetOverride(_projectKey, entry.Key, entry.Value);
            }
        }
        else
        {
            FloorCategoryOverrideStore floorCategoryOverrideStore = new();
            foreach (KeyValuePair<string, string> entry in floorAssignments)
            {
                floorCategoryOverrideStore.SetOverride(_projectKey, entry.Key, entry.Value);
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

        SharedParameterManager parameterManager = new(_document);
        using Transaction transaction = new(_document, "IMDF Export - Resolve Validation Issues");
        transaction.Start();
        parameterManager.EnsureParameters(warnings);

        foreach (long sourceElementId in distinctElementIds)
        {
            ElementId elementId = new(sourceElementId);
            Element? element = _document.GetElement(elementId);
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

    private static void ShowExportFailureDialog(Exception exception, ExportDialogResult request)
    {
        UiLanguage language = request.UiLanguage;
        string reportText = BuildFailureReport(exception);

        TaskDialog dialog = new(ProjectInfo.Name)
        {
            MainInstruction = UiLanguageText.Get(language, "Command.ExportFailed", "Export failed."),
            MainContent = UiLanguageText.Get(
                language,
                "Command.ExportFailed.Body",
                "The export could not be completed. You can save an error report as a text file."),
            ExpandedContent = reportText,
            AllowCancellation = true,
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        dialog.AddCommandLink(
            TaskDialogCommandLinkId.CommandLink1,
            UiLanguageText.Get(language, "Command.ExportFailed.SaveReport", "Save Error Report"));

        TaskDialogResult dialogResult = dialog.Show();
        if (dialogResult != TaskDialogResult.CommandLink1)
        {
            return;
        }

        SaveFailureReportToTextFile(reportText, request.OutputDirectory, language);
    }

    private static void SaveFailureReportToTextFile(string reportText, string? preferredDirectory, UiLanguage language)
    {
        string initialDirectory = ResolveReportDirectory(preferredDirectory);
        string defaultFileName = $"RevitGeoExporter-ExportError-{DateTime.Now:yyyyMMdd-HHmmss}.txt";

        using SaveFileDialog saveDialog = new()
        {
            Title = UiLanguageText.Get(language, "Command.ExportFailed.SaveReportTitle", "Save Export Error Report"),
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = "txt",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = initialDirectory,
            FileName = defaultFileName,
        };

        if (saveDialog.ShowDialog() != DialogResult.OK ||
            string.IsNullOrWhiteSpace(saveDialog.FileName))
        {
            return;
        }

        try
        {
            File.WriteAllText(saveDialog.FileName, reportText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            TaskDialog.Show(
                ProjectInfo.Name,
                UiLanguageText.Format(
                    language,
                    "Command.ExportFailed.ReportSaved",
                    "Error report saved.{0}{1}",
                    Environment.NewLine,
                    saveDialog.FileName));
        }
        catch (Exception ex)
        {
            TaskDialog.Show(
                ProjectInfo.Name,
                UiLanguageText.Format(
                    language,
                    "Command.ExportFailed.ReportSaveFailed",
                    "Failed to save error report.{0}{0}{1}",
                    Environment.NewLine,
                    ex.Message));
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

    private static string BuildBaselineKey(string projectKey, string? profileName)
    {
        string normalizedProfileName = profileName?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalizedProfileName)
            ? projectKey
            : $"{projectKey}__{normalizedProfileName}";
    }

    private static void ShowWarningsIfNeeded(IReadOnlyList<string> warnings, UiLanguage language)
    {
        if (warnings == null || warnings.Count == 0)
        {
            return;
        }

        string prefix = UiLanguageText.Get(
            language,
            "Validation.ResolveWarnings",
            "Some issues were encountered while applying validation fixes.");
        string warningText = string.Join(Environment.NewLine, warnings);
        string warningMessage = UiLanguageText.Format(
            language,
            "Validation.ResolveWarnings.Body",
            "{0}{1}{1}{2}",
            prefix,
            Environment.NewLine,
            warningText);
        TaskDialog.Show(ProjectInfo.Name, warningMessage);
    }

    private string? NavigateToValidationIssue(ValidationIssue issue)
    {
        if (issue == null)
        {
            return "Validation issue details were not available.";
        }

        if (_uiDocument == null)
        {
            return "Revit navigation is not available in this export session.";
        }

        try
        {
            if (issue.OwningViewId.HasValue)
            {
                Autodesk.Revit.DB.View? owningView = _document.GetElement(new ElementId(issue.OwningViewId.Value)) as Autodesk.Revit.DB.View;
                if (owningView != null &&
                    !owningView.IsTemplate &&
                    _uiDocument.ActiveView?.Id != owningView.Id)
                {
                    _uiDocument.ActiveView = owningView;
                }
            }

            if (!issue.SourceElementId.HasValue)
            {
                return issue.OwningViewId.HasValue
                    ? null
                    : "This validation issue is not attached to a specific Revit element.";
            }

            ElementId elementId = new(issue.SourceElementId.Value);
            Element? element = _document.GetElement(elementId);
            if (element == null)
            {
                return $"Element {issue.SourceElementId.Value} could not be found in the active Revit document.";
            }

            _uiDocument.Selection.SetElementIds(new List<ElementId> { elementId });
            _uiDocument.ShowElements(elementId);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
