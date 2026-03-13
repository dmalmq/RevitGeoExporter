using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Export;
using RevitGeoExporter.Resources;

namespace RevitGeoExporter.UI;

internal sealed class ExportPreviewController
{
    private readonly ExportPreviewRequest _request;
    private readonly ExportPreviewService _previewService;
    private readonly Dictionary<long, PreviewDisplayViewState> _cache = new();
    private readonly UiLanguage _language;
    private readonly IReadOnlyList<string> _supportedFloorCategories;

    private ViewPlan? _currentView;
    private PreviewViewData? _currentViewData;
    private PreviewDisplayViewState? _currentDisplayState;
    private PreviewAssignmentTarget? _assignmentTarget;
    private string _statusMessage = string.Empty;
    private string _basemapProviderStatus = string.Empty;
    private string? _pendingAssignmentFloorTypeName;

    public ExportPreviewController(ExportPreviewRequest request, ExportPreviewService previewService)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _previewService = previewService ?? throw new ArgumentNullException(nameof(previewService));
        _language = request.UiLanguage;
        _supportedFloorCategories = _previewService.GetSupportedFloorCategories().ToList();
        BasemapSettings = new PreviewBasemapSettings(request.PreviewBasemapUrlTemplate, request.PreviewBasemapAttribution);
        SetStatusMessage(T(
            "Mouse wheel zooms. Drag to pan. Click a feature to inspect it.",
            "マウス ホイールで拡大縮小し、ドラッグで移動できます。要素をクリックすると詳細を確認できます。"));
    }

    public UiLanguage Language => _language;

    public PreviewBasemapSettings BasemapSettings { get; }

    public IReadOnlyList<string> SupportedFloorCategories => _supportedFloorCategories;

    public PreviewDisplayViewState? CurrentDisplayState => _currentDisplayState;

    public PreviewViewData? CurrentViewData => _currentViewData;

    public bool HasPendingFloorCategoryChanges => _previewService.HasPendingFloorCategoryChanges;

    public string AssignmentSourceLabel => _previewService.GetAssignmentSourceLabel();

    public bool IsViewCached(ViewPlan view)
    {
        if (view == null)
        {
            throw new ArgumentNullException(nameof(view));
        }

        return _cache.ContainsKey(view.Id.Value);
    }

    public string BuildViewDisplayText(ViewPlan view)
    {
        if (view == null)
        {
            throw new ArgumentNullException(nameof(view));
        }

        string levelName = view.GenLevel?.Name ?? T("<no level>", "<レベルなし>");
        string levelLabel = T("Level", "レベル");
        return $"{view.Name} [{levelLabel}: {levelName}]";
    }

    public string BuildLoadingStatus(ViewPlan view)
    {
        if (view == null)
        {
            throw new ArgumentNullException(nameof(view));
        }

        return T(
            $"Loading preview for {view.Name}...",
            $"{view.Name} のプレビューを読み込んでいます...");
    }

    public PreviewDisplayViewState LoadView(ViewPlan view)
    {
        if (view == null)
        {
            throw new ArgumentNullException(nameof(view));
        }

        _currentView = view;
        if (!_cache.TryGetValue(view.Id.Value, out PreviewDisplayViewState? displayState))
        {
            PreviewViewData preview = _previewService.PrepareView(view, _request.FeatureTypes);
            displayState = PreviewDisplayViewStateBuilder.Build(preview, _request);
            _cache[view.Id.Value] = displayState;
        }

        _currentViewData = displayState.SourceViewData;
        _currentDisplayState = displayState;
        RestoreAssignmentTargetAfterReload();
        UpdateStatus(displayState.SourceViewData);
        return displayState;
    }

    public IReadOnlyList<PreviewLegendEntry> GetLegendEntries()
    {
        if (_currentViewData == null)
        {
            return Array.Empty<PreviewLegendEntry>();
        }

        return _currentViewData.Features
            .Where(feature => feature.FeatureType == ExportFeatureType.Unit)
            .GroupBy(feature => (feature.Category ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PreviewLegendEntry(
                string.IsNullOrWhiteSpace(group.Key) ? T("(uncategorized)", "(未分類)") : group.Key,
                group.Count(),
                group.First().FillColorHex))
            .ToList();
    }

    public PreviewDetailsSnapshot BuildDetailsSnapshot(PreviewFeatureData? feature)
    {
        if (_currentViewData == null)
        {
            return new PreviewDetailsSnapshot(Array.Empty<PreviewDetailEntry>(), string.Empty);
        }

        if (feature == null)
        {
            return new PreviewDetailsSnapshot(
                new[]
                {
                    new PreviewDetailEntry(T("View", "ビュー"), _currentViewData.ViewName),
                    new PreviewDetailEntry(T("Level", "レベル"), _currentViewData.LevelName),
                    new PreviewDetailEntry(T("Features", "要素数"), _currentViewData.Features.Count.ToString(CultureInfo.InvariantCulture)),
                    new PreviewDetailEntry(T("Unassigned floor types", "未割り当ての床タイプ"), _currentViewData.UnassignedFloors.Count.ToString(CultureInfo.InvariantCulture)),
                },
                T(
                    "Click a feature to inspect its metadata.",
                    "要素をクリックするとメタデータを確認できます。"));
        }

        List<PreviewDetailEntry> entries = new()
        {
            new PreviewDetailEntry(T("Feature type", "要素タイプ"), feature.FeatureType.ToString()),
            new PreviewDetailEntry(T("Category", "カテゴリ"), NullToPlaceholder(feature.Category)),
            new PreviewDetailEntry(T("Name", "名前"), NullToPlaceholder(feature.Name)),
            new PreviewDetailEntry(T("Restriction", "制限"), NullToPlaceholder(feature.Restriction)),
            new PreviewDetailEntry(T("Export ID", "Export ID"), NullToPlaceholder(feature.ExportId)),
            new PreviewDetailEntry(T("Source label", "ソース ラベル"), NullToPlaceholder(feature.SourceLabel)),
            new PreviewDetailEntry(
                T("Source element ID", "ソース要素 ID"),
                feature.SourceElementId.HasValue
                    ? feature.SourceElementId.Value.ToString(CultureInfo.InvariantCulture)
                    : "-"),
            new PreviewDetailEntry(T("Fill color", "塗り色"), $"#{feature.FillColorHex}"),
            new PreviewDetailEntry(T("Stroke color", "線色"), $"#{feature.StrokeColorHex}"),
        };

        if (feature.IsFloorDerived || feature.IsRoomDerived)
        {
            entries.Add(new PreviewDetailEntry(T("Mapping key", "マッピング キー"), NullToPlaceholder(feature.AssignmentMappingKey)));
            entries.Add(new PreviewDetailEntry(T("Parsed candidate", "解析候補"), NullToPlaceholder(feature.ParsedZoneCandidate)));
            entries.Add(new PreviewDetailEntry(T("Category resolution", "カテゴリ解決"), FormatResolutionSource(feature.CategoryResolutionSource)));
            entries.Add(new PreviewDetailEntry(T("Unassigned", "未割り当て"), feature.IsUnassignedFloor ? T("Yes", "はい") : T("No", "いいえ")));
        }

        return new PreviewDetailsSnapshot(entries, string.Empty);
    }

    public IReadOnlyList<string> GetWarnings()
    {
        return _currentViewData?.Warnings ?? Array.Empty<string>();
    }

    public IReadOnlyList<PreviewUnassignedFloorGroup> GetUnassignedFloors()
    {
        return _currentViewData?.UnassignedFloors ?? Array.Empty<PreviewUnassignedFloorGroup>();
    }

    public string BuildQuickSummaryText()
    {
        if (_currentViewData == null)
        {
            return string.Empty;
        }

        return T(
            $"{_currentViewData.Features.Count} features | {_currentViewData.Warnings.Count} warnings | {_currentViewData.UnassignedFloors.Count} unassigned floor types | {_currentViewData.AvailableSourceLabels.Count} source labels",
            $"{_currentViewData.Features.Count} 件 | 警告 {_currentViewData.Warnings.Count} 件 | 未割り当ての床タイプ {_currentViewData.UnassignedFloors.Count} 件 | ソース ラベル {_currentViewData.AvailableSourceLabels.Count} 件");
    }

    public string BuildCoordinateSummaryText()
    {
        if (_request.CoordinateMode == CoordinateExportMode.ConvertToTargetCrs)
        {
            return T(
                $"Preview coordinates: converted to EPSG {_request.TargetEpsg}",
                $"プレビュー座標: EPSG {_request.TargetEpsg} に変換");
        }

        int epsg = _request.SourceEpsg ?? _request.TargetEpsg;
        return T(
            $"Preview coordinates: shared coordinates (EPSG {epsg})",
            $"プレビュー座標: 共有座標系 (EPSG {epsg})");
    }

    public string BuildWarningsSummaryText()
    {
        int warningCount = _currentViewData?.Warnings.Count ?? 0;
        return warningCount == 0
            ? T("No warnings in this preview.", "このプレビューに警告はありません。")
            : T($"{warningCount} warnings in this preview.", $"このプレビューには {warningCount} 件の警告があります。");
    }

    public string BuildLegendEmptyText()
    {
        return T("No unit categories in this view.", "このビューに unit カテゴリはありません。");
    }

    public string BuildNoWarningsText()
    {
        return T("No warnings.", "警告はありません。");
    }

    public string BuildNoUnassignedFloorsText()
    {
        return T("No unassigned floor types in this preview.", "このプレビューに未割り当ての床タイプはありません。");
    }

    public string BuildViewInstructionText()
    {
        return T(
            "Click a feature to inspect metadata or stage assignment updates.",
            "要素をクリックするとメタデータを確認したり、割り当て更新を準備できます。");
    }

    public void UpdateBasemapProviderStatus(string? message)
    {
        _basemapProviderStatus = (message ?? string.Empty).Trim();
    }

    public bool IsBasemapToggleAvailable(bool canvasBasemapAvailable)
    {
        return _currentDisplayState?.MapContext.CanShowBasemap == true && canvasBasemapAvailable;
    }

    public bool IsSurveyPointToggleAvailable(bool canvasSurveyPointAvailable)
    {
        return _currentDisplayState?.DisplaySurveyPoint.HasValue == true && canvasSurveyPointAvailable;
    }

    public string GetBasemapInlineMessage(bool canvasBasemapAvailable, string? basemapUnavailableReason)
    {
        if (_currentDisplayState == null || IsBasemapToggleAvailable(canvasBasemapAvailable))
        {
            return string.Empty;
        }

        string reason = LocalizeBasemapMessage(basemapUnavailableReason);
        return reason.Length == 0
            ? L("Preview.BasemapUnavailable", "Basemap unavailable")
            : $"{L("Preview.BasemapUnavailable", "Basemap unavailable")}: {reason}";
    }

    public string GetSurveyPointInlineMessage(bool canvasSurveyPointAvailable)
    {
        if (_currentDisplayState == null || IsSurveyPointToggleAvailable(canvasSurveyPointAvailable))
        {
            return string.Empty;
        }

        return T(
            "Survey point is unavailable for this preview.",
            "このプレビューでは測量点を表示できません。");
    }

    public string BuildFooterStatus(
        bool canvasBasemapAvailable,
        string? basemapUnavailableReason,
        string? basemapAttribution,
        bool showBasemap,
        bool canvasSurveyPointAvailable,
        bool showSurveyPoint)
    {
        string basemapStatus = BuildBasemapStatusText(
            canvasBasemapAvailable,
            basemapUnavailableReason,
            basemapAttribution,
            showBasemap);
        string surveyPointStatus = BuildSurveyPointStatusText(
            canvasSurveyPointAvailable,
            showSurveyPoint);
        string extraStatus = string.Join(
            " | ",
            new[] { basemapStatus, surveyPointStatus }.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (string.IsNullOrWhiteSpace(_statusMessage))
        {
            return extraStatus;
        }

        return string.IsNullOrWhiteSpace(extraStatus)
            ? _statusMessage
            : $"{_statusMessage} | {extraStatus}";
    }

    public void SelectFeature(PreviewFeatureData? feature)
    {
        if (feature == null || !feature.SupportsFloorCategoryAssignment)
        {
            _assignmentTarget = null;
            return;
        }

        _assignmentTarget = CreateAssignmentTarget(feature);
    }

    public void SelectUnassignedFloor(string? floorTypeName, string? parsedCandidate)
    {
        if (string.IsNullOrWhiteSpace(floorTypeName))
        {
            _assignmentTarget = null;
            return;
        }

        _assignmentTarget = TryBuildAssignmentTarget(floorTypeName!)
            ?? new PreviewAssignmentTarget(
                floorTypeName!,
                parsedCandidate,
                currentCategory: "unspecified",
                hasOverride: false,
                isUnassigned: true);
    }

    public PreviewAssignmentState GetAssignmentState()
    {
        bool hasPendingChanges = _previewService.HasPendingFloorCategoryChanges;
        if (_assignmentTarget == null)
        {
            return new PreviewAssignmentState(
                selectedFloorTypeName: null,
                targetFloorType: "-",
                parsedCandidate: "-",
                currentResolution: "-",
                suggestedCategory: null,
                hintText: T(
                    "Select one or more floor types from the list, or click a floor-derived unit on the canvas, to stage category changes.",
                    "リストから床タイプを選択するか、キャンバス上の床由来ユニットをクリックしてカテゴリ変更を準備します。"),
                pendingMessage: hasPendingChanges
                    ? T("Unsaved assignment changes are staged.", "未保存の割り当て変更があります。")
                    : T("No assignment changes are staged.", "割り当て変更はまだありません。"),
                hasPendingChanges: hasPendingChanges,
                canChooseCategory: false,
                canAssign: false,
                canClear: false,
                canSave: hasPendingChanges,
                canDiscard: hasPendingChanges);
        }

        return new PreviewAssignmentState(
            selectedFloorTypeName: _assignmentTarget.FloorTypeName,
            targetFloorType: NullToPlaceholder(_assignmentTarget.FloorTypeName),
            parsedCandidate: NullToPlaceholder(_assignmentTarget.ParsedZoneCandidate),
            currentResolution: BuildAssignmentSummary(_assignmentTarget),
            suggestedCategory: _assignmentTarget.CurrentCategory,
            hintText: _assignmentTarget.HasOverride
                ? T(
                    "This floor type already uses a saved override. You can batch-assign or clear multiple selected floor types, then save the staged changes.",
                    "この床タイプには保存済みの上書きがあります。複数の床タイプをまとめて割り当て、保存できます。")
                : T(
                    "Assigning a category stages a project-specific override without changing the Revit model.",
                    "カテゴリ割り当ては Revit モデルを変更せず、プロジェクト固有の上書きを準備します。"),
            pendingMessage: hasPendingChanges
                ? T("Unsaved assignment changes are staged.", "未保存の割り当て変更があります。")
                : T("No assignment changes are staged.", "割り当て変更はまだありません。"),
            hasPendingChanges: hasPendingChanges,
            canChooseCategory: _supportedFloorCategories.Count > 0,
            canAssign: _supportedFloorCategories.Count > 0,
            canClear: _assignmentTarget.HasOverride,
            canSave: hasPendingChanges,
            canDiscard: hasPendingChanges);
    }

    public IReadOnlyList<string> GetSelectedFloorTypeNames(IEnumerable<string?> selectedFloorTypeNames)
    {
        List<string> names = (selectedFloorTypeNames ?? Enumerable.Empty<string?>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (names.Count == 0 && _assignmentTarget != null && !string.IsNullOrWhiteSpace(_assignmentTarget.FloorTypeName))
        {
            names.Add(_assignmentTarget.FloorTypeName);
        }

        return names;
    }

    public PreviewDisplayViewState? StageCategoryOverride(IReadOnlyList<string> floorTypeNames, string category)
    {
        if (floorTypeNames == null ||
            floorTypeNames.Count == 0 ||
            string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        foreach (string floorTypeName in floorTypeNames)
        {
            _previewService.StageFloorCategoryOverride(floorTypeName, category);
        }

        _pendingAssignmentFloorTypeName = floorTypeNames[0];
        _cache.Clear();
        PreviewDisplayViewState? displayState = ReloadCurrentView();
        SetStatusMessage(T(
            $"Pending floor override for {floorTypeNames.Count} floor type(s) -> {category}. Save assignments to persist it.",
            $"{floorTypeNames.Count} 件の床タイプに対する上書き ({category}) を準備しました。保存すると確定されます。"));
        return displayState;
    }

    public PreviewDisplayViewState? ClearCategoryOverride(IReadOnlyList<string> floorTypeNames)
    {
        if (floorTypeNames == null || floorTypeNames.Count == 0)
        {
            return null;
        }

        foreach (string floorTypeName in floorTypeNames)
        {
            _previewService.StageClearFloorCategoryOverride(floorTypeName);
        }

        _pendingAssignmentFloorTypeName = floorTypeNames[0];
        _cache.Clear();
        PreviewDisplayViewState? displayState = ReloadCurrentView();
        SetStatusMessage(T(
            $"Pending override removal for {floorTypeNames.Count} floor type(s). Save assignments to persist it.",
            $"{floorTypeNames.Count} 件の床タイプに対する上書き解除を準備しました。保存すると確定されます。"));
        return displayState;
    }

    public PreviewDisplayViewState? SavePendingAssignments()
    {
        if (!_previewService.HasPendingFloorCategoryChanges)
        {
            return null;
        }

        _previewService.ApplyPendingFloorCategoryOverrides();
        _cache.Clear();
        PreviewDisplayViewState? displayState = ReloadCurrentView();
        SetStatusMessage(T(
            "Saved preview floor assignments.",
            "プレビューの床割り当てを保存しました。"));
        return displayState;
    }

    public PreviewDisplayViewState? DiscardPendingAssignments()
    {
        if (!_previewService.HasPendingFloorCategoryChanges)
        {
            return null;
        }

        _previewService.DiscardPendingFloorCategoryOverrides();
        _cache.Clear();
        PreviewDisplayViewState? displayState = ReloadCurrentView();
        SetStatusMessage(T(
            "Discarded pending preview floor assignments.",
            "プレビューで保留中だった床割り当てを破棄しました。"));
        return displayState;
    }

    public void DiscardPendingChangesOnClose()
    {
        if (_previewService.HasPendingFloorCategoryChanges)
        {
            _previewService.DiscardPendingFloorCategoryOverrides();
        }
    }

    private PreviewDisplayViewState? ReloadCurrentView()
    {
        return _currentView == null ? null : LoadView(_currentView);
    }

    private void RestoreAssignmentTargetAfterReload()
    {
        string? pendingFloorTypeName = _pendingAssignmentFloorTypeName;
        _pendingAssignmentFloorTypeName = null;

        if (string.IsNullOrWhiteSpace(pendingFloorTypeName))
        {
            _assignmentTarget = null;
            return;
        }

        _assignmentTarget = TryBuildAssignmentTarget(pendingFloorTypeName!);
    }

    private PreviewAssignmentTarget CreateAssignmentTarget(PreviewFeatureData feature)
    {
        return new PreviewAssignmentTarget(
            feature.FloorTypeName ?? string.Empty,
            feature.ParsedZoneCandidate,
            feature.Category,
            feature.UsesFloorCategoryOverride,
            feature.IsUnassignedFloor);
    }

    private PreviewAssignmentTarget? TryBuildAssignmentTarget(string floorTypeName)
    {
        if (_currentViewData == null || string.IsNullOrWhiteSpace(floorTypeName))
        {
            return null;
        }

        PreviewFeatureData? feature = _currentViewData.Features
            .Where(candidate => string.Equals(candidate.AssignmentMappingKey, floorTypeName, StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.SupportsCategoryAssignment)
            .FirstOrDefault();

        return feature == null ? null : CreateAssignmentTarget(feature);
    }

    private void UpdateStatus(PreviewViewData preview)
    {
        string suffix = _previewService.HasPendingFloorCategoryChanges
            ? T(" | unsaved assignment changes", " | 未保存の割り当て変更あり")
            : string.Empty;
        SetStatusMessage(
            T(
                $"{preview.ViewName} [{preview.LevelName}] - {preview.Features.Count} preview features, {preview.UnassignedFloors.Count} unassigned floor types, {preview.AvailableSourceLabels.Count} source labels",
                $"{preview.ViewName} [{preview.LevelName}] - プレビュー要素 {preview.Features.Count} 件、未割り当ての床タイプ {preview.UnassignedFloors.Count} 件、ソース ラベル {preview.AvailableSourceLabels.Count} 件") + suffix);
    }

    private void SetStatusMessage(string message)
    {
        _statusMessage = message ?? string.Empty;
    }

    private string BuildBasemapStatusText(
        bool canvasBasemapAvailable,
        string? basemapUnavailableReason,
        string? basemapAttribution,
        bool showBasemap)
    {
        if (_currentDisplayState == null)
        {
            return string.Empty;
        }

        if (!canvasBasemapAvailable)
        {
            string reason = LocalizeBasemapMessage(basemapUnavailableReason);
            return reason.Length == 0
                ? L("Preview.BasemapUnavailable", "Basemap unavailable")
                : $"{L("Preview.BasemapUnavailable", "Basemap unavailable")}: {reason}";
        }

        if (!showBasemap)
        {
            return string.Empty;
        }

        string attribution = (basemapAttribution ?? string.Empty).Trim();
        string providerStatus = LocalizeBasemapMessage(_basemapProviderStatus);
        if (providerStatus.Length > 0)
        {
            return attribution.Length > 0
                ? $"{L("Preview.Basemap", "Basemap")}: {attribution} ({providerStatus})"
                : $"{L("Preview.Basemap", "Basemap")}: {providerStatus}";
        }

        return attribution.Length == 0
            ? L("Preview.Basemap", "Basemap")
            : $"{L("Preview.Basemap", "Basemap")}: {attribution}";
    }

    private string BuildSurveyPointStatusText(bool canvasSurveyPointAvailable, bool showSurveyPoint)
    {
        if (_currentDisplayState?.OutputSurveyPoint is not Point2D surveyPoint ||
            !IsSurveyPointToggleAvailable(canvasSurveyPointAvailable) ||
            !showSurveyPoint)
        {
            return string.Empty;
        }

        string label = L("Preview.SurveyPoint", "Survey point");
        string coordinates = string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.###}, {1:0.###}",
            surveyPoint.X,
            surveyPoint.Y);
        string crsLabel = _currentDisplayState.MapContext.OutputCrsLabel?.Trim() ?? string.Empty;
        return crsLabel.Length == 0
            ? $"{label}: {coordinates}"
            : $"{label}: {coordinates} ({crsLabel})";
    }

    private string LocalizeBasemapMessage(string? message)
    {
        string normalized = (message ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return normalized switch
        {
            "No basemap tile source is configured." => L("Preview.BasemapSourceMissing", "No basemap tile source is configured."),
            "Target EPSG could not be resolved for map preview." => L("Preview.BasemapTargetUnavailable", "Target CRS could not be resolved for map preview."),
            "Model source CRS could not be resolved for preview conversion." => L("Preview.BasemapSourceUnavailableForConversion", "Model source CRS could not be resolved for preview conversion."),
            "Model CRS could not be resolved for map preview." => L("Preview.BasemapUnavailableCrs", "Model CRS could not be resolved for map preview."),
            "The model's shared/site coordinate system could not be resolved to a supported CRS definition." => L("Preview.BasemapUnavailableCrs", "Model CRS could not be resolved for map preview."),
            "Web Mercator could not be resolved for map preview." => L("Preview.MapProjectionFailed", "Map preview projection failed."),
            "Map preview projection failed." => L("Preview.MapProjectionFailed", "Map preview projection failed."),
            "Basemap loading failed; showing model only." => L("Preview.BasemapLoadFailed", "Basemap loading failed; showing model only."),
            _ => normalized,
        };
    }

    private string FormatResolutionSource(FloorCategoryResolutionSource? resolutionSource)
    {
        return resolutionSource switch
        {
            FloorCategoryResolutionSource.Catalog => T("Catalog", "カタログ"),
            FloorCategoryResolutionSource.Override => T("Override", "上書き"),
            FloorCategoryResolutionSource.FallbackUnspecified => T("Fallback", "フォールバック"),
            _ => "-",
        };
    }

    private string BuildAssignmentSummary(PreviewAssignmentTarget target)
    {
        string category = NullToPlaceholder(target.CurrentCategory);
        string source = target.HasOverride
            ? T("Override", "上書き")
            : target.IsUnassigned
                ? T("Fallback", "フォールバック")
                : T("Catalog", "カタログ");
        return $"{category} ({source})";
    }

    private static string NullToPlaceholder(string? value)
    {
        if (value == null)
        {
            return "-";
        }

        string trimmed = value.Trim();
        return trimmed.Length == 0 ? "-" : trimmed;
    }

    private string L(string key, string fallback) => LocalizedTextProvider.Get(_language, key, fallback);

    private string T(string english, string japanese)
    {
        return UiLanguageText.Select(_language, english, japanese);
    }

    private sealed class PreviewAssignmentTarget
    {
        public PreviewAssignmentTarget(
            string floorTypeName,
            string? parsedZoneCandidate,
            string? currentCategory,
            bool hasOverride,
            bool isUnassigned)
        {
            FloorTypeName = floorTypeName ?? throw new ArgumentNullException(nameof(floorTypeName));
            ParsedZoneCandidate = parsedZoneCandidate;
            CurrentCategory = currentCategory;
            HasOverride = hasOverride;
            IsUnassigned = isUnassigned;
        }

        public string FloorTypeName { get; }

        public string? ParsedZoneCandidate { get; }

        public string? CurrentCategory { get; }

        public bool HasOverride { get; }

        public bool IsUnassigned { get; }
    }
}

internal sealed class PreviewLegendEntry
{
    public PreviewLegendEntry(string label, int count, string fillColorHex)
    {
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Count = count;
        FillColorHex = fillColorHex ?? throw new ArgumentNullException(nameof(fillColorHex));
    }

    public string Label { get; }

    public int Count { get; }

    public string FillColorHex { get; }
}

internal sealed class PreviewDetailEntry
{
    public PreviewDetailEntry(string label, string value)
    {
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Label { get; }

    public string Value { get; }
}

internal sealed class PreviewDetailsSnapshot
{
    public PreviewDetailsSnapshot(IReadOnlyList<PreviewDetailEntry> entries, string helperText)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        HelperText = helperText ?? throw new ArgumentNullException(nameof(helperText));
    }

    public IReadOnlyList<PreviewDetailEntry> Entries { get; }

    public string HelperText { get; }
}

internal sealed class PreviewAssignmentState
{
    public PreviewAssignmentState(
        string? selectedFloorTypeName,
        string targetFloorType,
        string parsedCandidate,
        string currentResolution,
        string? suggestedCategory,
        string hintText,
        string pendingMessage,
        bool hasPendingChanges,
        bool canChooseCategory,
        bool canAssign,
        bool canClear,
        bool canSave,
        bool canDiscard)
    {
        SelectedFloorTypeName = selectedFloorTypeName;
        TargetFloorType = targetFloorType ?? throw new ArgumentNullException(nameof(targetFloorType));
        ParsedCandidate = parsedCandidate ?? throw new ArgumentNullException(nameof(parsedCandidate));
        CurrentResolution = currentResolution ?? throw new ArgumentNullException(nameof(currentResolution));
        SuggestedCategory = suggestedCategory;
        HintText = hintText ?? throw new ArgumentNullException(nameof(hintText));
        PendingMessage = pendingMessage ?? throw new ArgumentNullException(nameof(pendingMessage));
        HasPendingChanges = hasPendingChanges;
        CanChooseCategory = canChooseCategory;
        CanAssign = canAssign;
        CanClear = canClear;
        CanSave = canSave;
        CanDiscard = canDiscard;
    }

    public string? SelectedFloorTypeName { get; }

    public string TargetFloorType { get; }

    public string ParsedCandidate { get; }

    public string CurrentResolution { get; }

    public string? SuggestedCategory { get; }

    public string HintText { get; }

    public string PendingMessage { get; }

    public bool HasPendingChanges { get; }

    public bool CanChooseCategory { get; }

    public bool CanAssign { get; }

    public bool CanClear { get; }

    public bool CanSave { get; }

    public bool CanDiscard { get; }
}
