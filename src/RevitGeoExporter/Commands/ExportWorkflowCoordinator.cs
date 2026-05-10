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
using RevitGeoExporter.Core.Schema;
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
            previewRequest.UnitGeometrySource,
            previewRequest.UnitAttributeSource,
            previewRequest.RoomCategoryParameterName,
            previewRequest.GeometryRepairOptions,
            previewRequest.LinkExportOptions,
            previewRequest.ActiveSchemaProfile,
            previewRequest.SimplifyStairUnits,
            previewRequest.SimplifyEscalatorUnits,
            previewRequest.Use3DSectionBoxExport,
            previewRequest.SectionBoxAboveFloorMeters,
            previewRequest.SectionBoxBelowFloorMeters,
            previewRequest.Keep3DTempViewsForDebug);

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
                        PackagingMode = request.PackagingMode,
                        ValidateAfterWrite = request.ValidateAfterWrite,
                        GenerateQgisArtifacts = request.GenerateQgisArtifacts,
                        PostExportActions = request.PostExportActions.Clone(),
                    },
                    request.SelectedProfileName,
                    BuildBaselineKey(_projectKey, request.SelectedProfileName),
                    request.IncrementalExportMode,
                    request.CoordinateMode,
                    coordinateInfo.ResolvedSourceEpsg,
                    coordinateInfo.SiteCoordinateSystemId,
                    coordinateInfo.SiteCoordinateSystemDefinition,
                    request.UnitSource,
                    request.UnitGeometrySource,
                    request.UnitAttributeSource,
                    request.RoomCategoryParameterName,
                    request.LinkExportOptions,
                    request.ActiveSchemaProfile,
                    request.ActiveValidationPolicyProfile,
                    request.SimplifyStairUnits,
                    request.SimplifyEscalatorUnits,
                    request.Use3DSectionBoxExport,
                    request.SectionBoxAboveFloorMeters,
                    request.SectionBoxBelowFloorMeters,
                    request.Keep3DTempViewsForDebug);
                session.OutputFormat = request.OutputFormat;

                ExportValidationRequest validationRequest = snapshotBuilder.Build(session);
                validationResult = validationService.Validate(validationRequest);
                SharedCoordinateValidationResult coordinateValidation = new SharedCoordinateValidator()
                    .Validate(_document)
                    .ApplyPolicy(request.ActiveValidationPolicyProfile);
                ExportReadinessSummary readinessSummary = new ExportReadinessSummaryBuilder().Build(
                    validationRequest,
                    validationResult,
                    ZoneCatalog.CreateDefault(),
                    coordinateValidation.Findings.Count(finding => finding.Severity == ValidationSeverity.Error),
                    coordinateValidation.Findings.Count(finding => finding.Severity == ValidationSeverity.Warning));
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

                if (!ConfirmIncrementalExportPlan(exporter.PreviewExecutionSummary(session), request.UiLanguage))
                {
                    return Result.Cancelled;
                }

                break;
            }

            FloorGeoPackageExportResult result = CompleteExport(session, validationResult, request);

            using ExportResultForm resultForm = new(result, request.OutputDirectory, request.UiLanguage);
            _ = resultForm.ShowDialog();
            return Result.Succeeded;
        }
        catch (OperationCanceledException)
        {
            TaskDialog.Show(
                ProjectInfo.Name,
                UiLanguageText.Get(request.UiLanguage, "Command.ExportCancelled", "Export was cancelled. Partial output may have been written to the output directory."));
            return Result.Cancelled;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            ShowExportFailureDialog(ex, request);
            return Result.Failed;
        }
    }

    public void ShowBatchExport(ModelCoordinateInfo coordinateInfo, WinForms.IWin32Window? owner = null)
    {
        IReadOnlyList<ExportProfile> profiles = _profileStore.LoadWithDiagnostics(_projectKey).Value;
        using BatchExportForm form = new(profiles, CommandLanguageResolver.Resolve());
        if (form.ShowDialog(owner) != DialogResult.OK || form.Result == null)
        {
            return;
        }

        BatchPreflightSummary preflight = PreflightBatchJobs(form.Result);
        ExportJobManifest? executableManifest = ResolveBatchPreflightManifest(preflight, CommandLanguageResolver.Resolve());
        if (executableManifest == null)
        {
            return;
        }

        BatchExecutionSummary summary = RunBatchExport(executableManifest, coordinateInfo);
        ShowBatchSummary(summary);
    }

    private BatchPreflightSummary PreflightBatchJobs(ExportJobManifest manifest)
    {
        IReadOnlyList<ViewPlan> availableViews = new ViewCollector().GetExportablePlanViews(_document);
        HashSet<long> availableViewIds = new(availableViews.Select(view => view.Id.Value));
        IReadOnlyList<ExportProfile> profiles = _profileStore.LoadWithDiagnostics(_projectKey).Value;
        Dictionary<string, ExportProfile> profilesByName = profiles
            .GroupBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        BatchPreflightSummary summary = new();
        foreach (ExportJobManifestItem job in manifest.Jobs)
        {
            string profileName = job.ProfileName?.Trim() ?? string.Empty;
            if (profileName.Length == 0)
            {
                summary.InvalidJobs.Add(new BatchPreflightIssue(job, "<unspecified>", "The batch job did not specify a profile name."));
                continue;
            }

            if (!profilesByName.TryGetValue(profileName, out ExportProfile? profile))
            {
                summary.InvalidJobs.Add(new BatchPreflightIssue(job, profileName, "The saved export profile could not be found."));
                continue;
            }

            if (profile.SelectedViewIds == null || profile.SelectedViewIds.Count == 0)
            {
                summary.InvalidJobs.Add(new BatchPreflightIssue(job, profileName, "The saved profile has no persisted view selections."));
                continue;
            }

            List<long> missingViewIds = profile.SelectedViewIds
                .Distinct()
                .Where(viewId => !availableViewIds.Contains(viewId))
                .OrderBy(viewId => viewId)
                .ToList();
            if (missingViewIds.Count > 0)
            {
                summary.InvalidJobs.Add(new BatchPreflightIssue(
                    job,
                    profileName,
                    $"Saved view ids are no longer available: {string.Join(", ", missingViewIds)}"));
                continue;
            }

            ExportDialogSettings settings = profile.ToSettings();
            string? outputOverride = job.OutputDirectoryOverride;
            string outputDirectory = string.IsNullOrWhiteSpace(outputOverride)
                ? settings.OutputDirectory?.Trim() ?? string.Empty
                : outputOverride!.Trim();
            if (outputDirectory.Length == 0)
            {
                summary.InvalidJobs.Add(new BatchPreflightIssue(job, profileName, "No output directory is configured."));
                continue;
            }

            summary.ValidJobs.Add(job);
        }

        return summary;
    }

    private BatchExecutionSummary RunBatchExport(ExportJobManifest manifest, ModelCoordinateInfo coordinateInfo)
    {
        IReadOnlyList<ViewPlan> availableViews = new ViewCollector().GetExportablePlanViews(_document);
        Dictionary<long, ViewPlan> viewsById = availableViews
            .GroupBy(view => view.Id.Value)
            .Select(group => group.First())
            .ToDictionary(view => view.Id.Value);
        IReadOnlyList<ExportProfile> profiles = _profileStore.LoadWithDiagnostics(_projectKey).Value;

        BatchExecutionSummary summary = new();
        foreach (ExportJobManifestItem job in manifest.Jobs)
        {
            BatchJobExecutionResult jobResult = ExecuteBatchJob(job, profiles, viewsById, coordinateInfo);
            summary.Jobs.Add(jobResult);
        }

        return summary;
    }

    private BatchJobExecutionResult ExecuteBatchJob(
        ExportJobManifestItem job,
        IReadOnlyList<ExportProfile> profiles,
        IReadOnlyDictionary<long, ViewPlan> viewsById,
        ModelCoordinateInfo coordinateInfo)
    {
        string profileName = job.ProfileName?.Trim() ?? string.Empty;
        if (profileName.Length == 0)
        {
            return BatchJobExecutionResult.Failed("<unspecified>", "The batch job did not specify a profile name.");
        }

        ExportProfile? profile = profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, profileName, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
        {
            return BatchJobExecutionResult.Failed(profileName, "The saved export profile could not be found.");
        }

        if (profile.SelectedViewIds == null || profile.SelectedViewIds.Count == 0)
        {
            return BatchJobExecutionResult.Failed(profileName, "The saved export profile does not have persisted view selections. Resave the profile and try again.");
        }

        List<ViewPlan> selectedViews = new();
        foreach (long viewId in profile.SelectedViewIds.Distinct().OrderBy(id => id))
        {
            if (!viewsById.TryGetValue(viewId, out ViewPlan? view))
            {
                return BatchJobExecutionResult.Failed(profileName, $"Saved view id {viewId} is no longer available in the current document.");
            }

            selectedViews.Add(view);
        }

        ExportDialogSettings settings = profile.ToSettings();
        string? outputOverride = job.OutputDirectoryOverride;
        if (!string.IsNullOrWhiteSpace(outputOverride))
        {
            settings.OutputDirectory = outputOverride!.Trim();
        }

        ExportDialogResult request = new(
            selectedViews,
            settings.OutputDirectory,
            settings.TargetEpsg,
            settings.FeatureTypes,
            settings.IncrementalExportMode,
            settings.GenerateDiagnosticsReport,
            settings.GeneratePackageOutput,
            settings.IncludePackageLegend,
            settings.PackagingMode,
            settings.ValidateAfterWrite,
            settings.GenerateQgisArtifacts,
            settings.PostExportActions,
            settings.GeometryRepairOptions,
            profile.Name,
            settings.UiLanguage,
            settings.CoordinateMode,
            settings.UnitSource,
            settings.UnitGeometrySource,
            settings.UnitAttributeSource,
            settings.RoomCategoryParameterName,
            settings.SimplifyStairUnits,
            settings.SimplifyEscalatorUnits,
            settings.LinkExportOptions,
            SchemaProfile.ResolveActive(settings.SchemaProfiles, settings.ActiveSchemaProfileName),
            ValidationPolicyProfile.NormalizeProfiles(settings.ValidationPolicyProfiles)
                .First(profileItem => string.Equals(
                    profileItem.Name,
                    ValidationPolicyProfile.ResolveActiveName(settings.ValidationPolicyProfiles, settings.ActiveValidationPolicyProfileName),
                    StringComparison.OrdinalIgnoreCase)),
            settings.Use3DSectionBoxExport,
            settings.SectionBoxAboveFloorMeters,
            settings.SectionBoxBelowFloorMeters,
            settings.Keep3DTempViewsForDebug)
        {
            OutputFormat = settings.OutputFormat,
        };

        try
        {
            FloorGeoPackageExporter exporter = new(_document);
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
                    PackagingMode = request.PackagingMode,
                    ValidateAfterWrite = request.ValidateAfterWrite,
                    GenerateQgisArtifacts = request.GenerateQgisArtifacts,
                    PostExportActions = request.PostExportActions.Clone(),
                },
                request.SelectedProfileName,
                BuildBaselineKey(_projectKey, request.SelectedProfileName),
                request.IncrementalExportMode,
                request.CoordinateMode,
                coordinateInfo.ResolvedSourceEpsg,
                coordinateInfo.SiteCoordinateSystemId,
                coordinateInfo.SiteCoordinateSystemDefinition,
                request.UnitSource,
                request.UnitGeometrySource,
                request.UnitAttributeSource,
                request.RoomCategoryParameterName,
                request.LinkExportOptions,
                request.ActiveSchemaProfile,
                request.ActiveValidationPolicyProfile,
                request.SimplifyStairUnits,
                request.SimplifyEscalatorUnits,
                request.Use3DSectionBoxExport,
                request.SectionBoxAboveFloorMeters,
                request.SectionBoxBelowFloorMeters,
                request.Keep3DTempViewsForDebug);
            session.OutputFormat = request.OutputFormat;

            ExportValidationResult validationResult = new ExportValidationService()
                .Validate(new ExportValidationSnapshotBuilder().Build(session));
            FloorGeoPackageExportResult result = CompleteExport(session, validationResult, request);
            return BatchJobExecutionResult.Completed(
                profileName,
                result.ArtifactResults.Count(artifact => artifact.Disposition == ArtifactDisposition.Written),
                result.ArtifactResults.Count(artifact => artifact.Disposition == ArtifactDisposition.ReusedFromBaseline),
                result.Warnings.Count);
        }
        catch (Exception ex)
        {
            return BatchJobExecutionResult.Failed(profileName, ex.Message);
        }
    }

    private FloorGeoPackageExportResult CompleteExport(
        PreparedExportSession session,
        ExportValidationResult validationResult,
        ExportDialogResult request)
    {
        FloorGeoPackageExporter exporter = new(_document);
        FloorGeoPackageExportResult result;
        Stopwatch stopwatch = Stopwatch.StartNew();
        Stopwatch phaseStopwatch = Stopwatch.StartNew();
        using (ExportProgressForm progressForm = new())
        {
            progressForm.Show();
            progressForm.Refresh();

            result = exporter.WritePreparedExport(
                session,
                progressCallback: update => progressForm.UpdateProgress(update),
                cancellationToken: progressForm.CancellationToken);

            progressForm.Close();
        }

        phaseStopwatch.Stop();
        result.AddPhaseTiming("Artifact writing", phaseStopwatch.Elapsed);

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
                phaseStopwatch.Restart();
                ExportDiagnosticsWriter diagnosticsWriter = new();
                string diagnosticsPath = diagnosticsWriter.WriteJson(request.OutputDirectory, diagnosticsReport);
                phaseStopwatch.Stop();
                result.AddPhaseTiming("Diagnostics report", phaseStopwatch.Elapsed);
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
        phaseStopwatch.Restart();
        ExportPackageResult packageResult = packageService.BuildPackage(session, diagnosticsReport, result);
        phaseStopwatch.Stop();
        result.AddPhaseTiming("Package build", phaseStopwatch.Elapsed);
        result.SetPackagePaths(packageResult.PackageDirectory, packageResult.ManifestPath);
        result.SetPackageValidationResult(packageResult.ValidationResult);

        phaseStopwatch.Restart();
        ExportBaselineStore baselineStore = new();
        ExportBaselineLoadResult baseline = baselineStore.Load(session.BaselineKey);
        result.AddWarnings(baseline.Warnings);
        ChangeSummaryService changeSummaryService = new();
        result.SetChangeSummary(changeSummaryService.Compare(
            baseline.Snapshot,
            baseline.Report,
            diagnosticsReport,
            baseline.Manifest,
            packageResult.Manifest,
            result.ExecutionSummary?.ChangedViewCount ?? session.Prepared.Views.Count,
            result.ExecutionSummary?.ReusedViewCount ?? 0,
            result.ArtifactResults.Count(artifact => artifact.Disposition == ArtifactDisposition.Written),
            result.ArtifactResults.Count(artifact => artifact.Disposition == ArtifactDisposition.ReusedFromBaseline),
            result.ExecutionSummary?.MissingBaselineArtifactCount ?? 0,
            result.ExecutionSummary?.FullRewriteReason));

        bool canReplaceBaseline = result.PendingBaselineSnapshot != null &&
                                  (packageResult.ValidationResult == null || !packageResult.ValidationResult.HasErrors);
        if (canReplaceBaseline)
        {
            baselineStore.Save(session.BaselineKey, diagnosticsReport, packageResult.Manifest, result.PendingBaselineSnapshot!);
        }
        else if (packageResult.ValidationResult?.HasErrors == true)
        {
            result.AddWarning("Package validation errors prevented the export baseline from being replaced.");
        }

        phaseStopwatch.Stop();
        result.AddPhaseTiming("Baseline update", phaseStopwatch.Elapsed);

        if (request.GenerateDiagnosticsReport && !string.IsNullOrWhiteSpace(result.DiagnosticsReportPath))
        {
            diagnosticsReport.PhaseTimings = result.PhaseTimings.ToList();
            diagnosticsReport.PackageValidationResult = result.PackageValidationResult;
            try
            {
                File.WriteAllText(result.DiagnosticsReportPath, Newtonsoft.Json.JsonConvert.SerializeObject(diagnosticsReport, Newtonsoft.Json.Formatting.Indented));
                if (!string.IsNullOrWhiteSpace(result.PackageDirectoryPath))
                {
                    string packagedDiagnosticsPath = Path.Combine(result.PackageDirectoryPath, Path.GetFileName(result.DiagnosticsReportPath));
                    if (File.Exists(packagedDiagnosticsPath))
                    {
                        File.Copy(result.DiagnosticsReportPath, packagedDiagnosticsPath, overwrite: true);
                    }
                }
            }
            catch (Exception diagnosticsException)
            {
                result.AddWarning($"Diagnostics report timing details could not be refreshed: {diagnosticsException.Message}");
            }
        }

        return result;
    }

    private static void ShowBatchSummary(BatchExecutionSummary summary)
    {
        List<BatchJobResultRow> rows = summary.Jobs.Select(job =>
            new BatchJobResultRow(
                job.ProfileName,
                job.Succeeded,
                job.WrittenArtifactCount,
                job.ReusedArtifactCount,
                job.WarningCount,
                job.Message)).ToList();

        using BatchExportResultForm form = new(rows);
        _ = form.ShowDialog();
    }

    private static ExportJobManifest? ResolveBatchPreflightManifest(BatchPreflightSummary summary, UiLanguage language)
    {
        if (summary.InvalidJobs.Count == 0)
        {
            TaskDialog readyDialog = new(ProjectInfo.Name)
            {
                MainInstruction = UiLanguageText.Select(language, "Batch preflight passed.", "バッチ事前チェックが完了しました。"),
                MainContent = UiLanguageText.Select(
                    language,
                    $"Ready to run {summary.ValidJobs.Count} job(s).",
                    $"{summary.ValidJobs.Count} 件のジョブを実行できます。"),
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                AllowCancellation = true,
            };
            return readyDialog.Show() == TaskDialogResult.Ok
                ? new ExportJobManifest { Jobs = summary.ValidJobs.ToList() }
                : null;
        }

        string issueLines = string.Join(
            Environment.NewLine,
            summary.InvalidJobs
                .Take(8)
                .Select(issue => $"- {issue.ProfileName}: {issue.Message}"));
        if (summary.InvalidJobs.Count > 8)
        {
            issueLines += $"{Environment.NewLine}- ...";
        }

        TaskDialog dialog = new(ProjectInfo.Name)
        {
            MainInstruction = UiLanguageText.Select(language, "Batch preflight found invalid jobs.", "バッチ事前チェックで無効なジョブが見つかりました。"),
            MainContent = UiLanguageText.Select(
                language,
                $"Valid jobs: {summary.ValidJobs.Count}{Environment.NewLine}Invalid jobs: {summary.InvalidJobs.Count}{Environment.NewLine}{Environment.NewLine}{issueLines}",
                $"有効なジョブ: {summary.ValidJobs.Count}{Environment.NewLine}無効なジョブ: {summary.InvalidJobs.Count}{Environment.NewLine}{Environment.NewLine}{issueLines}"),
            AllowCancellation = true,
            CommonButtons = TaskDialogCommonButtons.Cancel,
        };

        if (summary.ValidJobs.Count > 0)
        {
            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink1,
                UiLanguageText.Select(language, "Run valid jobs only", "有効なジョブのみ実行"));
        }

        return dialog.Show() == TaskDialogResult.CommandLink1
            ? new ExportJobManifest { Jobs = summary.ValidJobs.ToList() }
            : null;
    }

    private static bool ConfirmIncrementalExportPlan(ExportExecutionSummary summary, UiLanguage language)
    {
        if (summary.IncrementalExportMode != IncrementalExportMode.ChangedViewsOnly)
        {
            return true;
        }

        string content = UiLanguageText.Select(
            language,
            $"Changed views: {summary.ChangedViewCount}{Environment.NewLine}" +
            $"Reusable views: {summary.ReusedViewCount}{Environment.NewLine}" +
            $"Missing reusable artifacts: {summary.MissingBaselineArtifactCount}" +
            (string.IsNullOrWhiteSpace(summary.FullRewriteReason)
                ? string.Empty
                : $"{Environment.NewLine}{Environment.NewLine}Full rewrite reason: {summary.FullRewriteReason}"),
            $"変更されたビュー: {summary.ChangedViewCount}{Environment.NewLine}" +
            $"再利用可能なビュー: {summary.ReusedViewCount}{Environment.NewLine}" +
            $"再利用できない既存成果物: {summary.MissingBaselineArtifactCount}" +
            (string.IsNullOrWhiteSpace(summary.FullRewriteReason)
                ? string.Empty
                : $"{Environment.NewLine}{Environment.NewLine}全再出力の理由: {summary.FullRewriteReason}"));

        TaskDialog dialog = new(ProjectInfo.Name)
        {
            MainInstruction = UiLanguageText.Select(language, "Incremental export preview", "差分エクスポート プレビュー"),
            MainContent = content,
            CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
            AllowCancellation = true,
        };

        return dialog.Show() == TaskDialogResult.Ok;
    }

    private sealed class BatchExecutionSummary
    {
        public List<BatchJobExecutionResult> Jobs { get; } = new();
    }

    private sealed class BatchPreflightSummary
    {
        public List<ExportJobManifestItem> ValidJobs { get; } = new();

        public List<BatchPreflightIssue> InvalidJobs { get; } = new();
    }

    private sealed class BatchPreflightIssue
    {
        public BatchPreflightIssue(ExportJobManifestItem job, string profileName, string message)
        {
            Job = job;
            ProfileName = profileName;
            Message = message;
        }

        public ExportJobManifestItem Job { get; }

        public string ProfileName { get; }

        public string Message { get; }
    }

    private sealed class BatchJobExecutionResult
    {
        private BatchJobExecutionResult(
            string profileName,
            bool succeeded,
            string message,
            int writtenArtifactCount,
            int reusedArtifactCount,
            int warningCount)
        {
            ProfileName = profileName;
            Succeeded = succeeded;
            Message = message;
            WrittenArtifactCount = writtenArtifactCount;
            ReusedArtifactCount = reusedArtifactCount;
            WarningCount = warningCount;
        }

        public string ProfileName { get; }

        public bool Succeeded { get; }

        public string Message { get; }

        public int WrittenArtifactCount { get; }

        public int ReusedArtifactCount { get; }

        public int WarningCount { get; }

        public static BatchJobExecutionResult Failed(string profileName, string message)
        {
            return new BatchJobExecutionResult(profileName, succeeded: false, message, 0, 0, 0);
        }

        public static BatchJobExecutionResult Completed(string profileName, int writtenArtifactCount, int reusedArtifactCount, int warningCount)
        {
            return new BatchJobExecutionResult(profileName, succeeded: true, string.Empty, writtenArtifactCount, reusedArtifactCount, warningCount);
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
