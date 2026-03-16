using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Validation;

namespace RevitGeoExporter.UI;

public sealed class ValidationIssueResolutionForm : Form
{
    private readonly UiLanguage _language;
    private readonly IReadOnlyList<FloorAssignmentCandidate> _floorCandidates;
    private readonly IReadOnlyList<StableIdCandidate> _idCandidates;
    private readonly int _unfixableDuplicateGroupCount;
    private readonly IReadOnlyList<string> _supportedCategories;

    private readonly Label _summaryLabel = new();
    private readonly DataGridView _floorGrid = new();
    private readonly DataGridView _idGrid = new();
    private readonly Button _applyButton = new();
    private readonly Button _cancelButton = new();

    public ValidationIssueResolutionForm(ExportValidationRequest request, UiLanguage language)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        _language = language;
        ZoneCatalog zoneCatalog = ZoneCatalog.CreateDefault();
        _supportedCategories = new FloorCategoryResolver(zoneCatalog).SupportedCategories;
        _floorCandidates = BuildFloorCandidates(request, zoneCatalog, _supportedCategories);
        _idCandidates = BuildStableIdCandidates(request, out _unfixableDuplicateGroupCount);

        SelectedFloorAssignments = new Dictionary<string, string>(StringComparer.Ordinal);
        SelectedElementIdsToRegenerate = Array.Empty<long>();

        InitializeComponents();
        Populate();
    }

    public IReadOnlyDictionary<string, string> SelectedFloorAssignments { get; private set; }

    public IReadOnlyList<long> SelectedElementIdsToRegenerate { get; private set; }

    public static bool HasResolvableIssues(ExportValidationRequest request)
    {
        if (request is null)
        {
            return false;
        }

        ZoneCatalog zoneCatalog = ZoneCatalog.CreateDefault();
        IReadOnlyList<string> categories = new FloorCategoryResolver(zoneCatalog).SupportedCategories;
        if (BuildFloorCandidates(request, zoneCatalog, categories).Count > 0)
        {
            return true;
        }

        return BuildStableIdCandidates(request, out _) .Count > 0;
    }

    private void InitializeComponents()
    {
        Text = T("Resolve Validation Issues", "検証の問題を解消");
        Width = 980;
        Height = 720;
        MinimumSize = new Size(820, 540);
        StartPosition = FormStartPosition.CenterParent;

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        Controls.Add(root);

        _summaryLabel.Dock = DockStyle.Fill;
        _summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(_summaryLabel, 0, 0);

        TabControl tabs = new()
        {
            Dock = DockStyle.Fill,
        };
        TabPage floorTab = new(T("Floor Mappings", "床マッピング"));
        floorTab.Controls.Add(BuildFloorPanel());
        tabs.TabPages.Add(floorTab);

        TabPage idTab = new(T("Stable IDs", "安定ID"));
        idTab.Controls.Add(BuildIdPanel());
        tabs.TabPages.Add(idTab);
        root.Controls.Add(tabs, 0, 1);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0),
        };

        _cancelButton.Width = 110;
        _cancelButton.Height = 30;
        _cancelButton.Text = T("Cancel", "キャンセル");
        _cancelButton.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(_cancelButton);

        _applyButton.Width = 140;
        _applyButton.Height = 30;
        _applyButton.Text = T("Apply Fixes", "修正を適用");
        _applyButton.Click += (_, _) => ApplySelections();
        actions.Controls.Add(_applyButton);

        root.Controls.Add(actions, 0, 2);
        AcceptButton = _applyButton;
        CancelButton = _cancelButton;
    }

    private Control BuildFloorPanel()
    {
        if (_floorCandidates.Count == 0)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = T("No missing floor mappings can be resolved here.", "ここで解消できる未割り当ての床マッピングはありません。"),
            };
        }

        _floorGrid.Dock = DockStyle.Fill;
        _floorGrid.AutoGenerateColumns = false;
        _floorGrid.AllowUserToAddRows = false;
        _floorGrid.AllowUserToDeleteRows = false;
        _floorGrid.AllowUserToResizeRows = false;
        _floorGrid.RowHeadersVisible = false;
        _floorGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _floorGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FloorTypeName",
            HeaderText = T("Floor Type Name", "床タイプ名"),
            FillWeight = 42f,
            ReadOnly = true,
        });
        _floorGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Occurrences",
            HeaderText = T("Count", "件数"),
            FillWeight = 10f,
            ReadOnly = true,
        });
        _floorGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "SuggestedCategory",
            HeaderText = T("Suggested", "候補"),
            FillWeight = 18f,
            ReadOnly = true,
        });

        DataGridViewComboBoxColumn assignColumn = new()
        {
            Name = "AssignCategory",
            HeaderText = T("Assign Category", "割り当てカテゴリ"),
            FillWeight = 30f,
            FlatStyle = FlatStyle.Flat,
        };
        assignColumn.Items.Add(string.Empty);
        foreach (string category in _supportedCategories)
        {
            assignColumn.Items.Add(category);
        }

        _floorGrid.Columns.Add(assignColumn);
        return _floorGrid;
    }

    private Control BuildIdPanel()
    {
        if (_idCandidates.Count == 0)
        {
            string message = _unfixableDuplicateGroupCount > 0
                ? T(
                    "Duplicate IDs were found, but they appear to come from the same source element being exported more than once across the selected views. Those cases cannot be auto-regenerated here.",
                    "重複IDは見つかりましたが、選択したビュー間で同じ元要素が複数回書き出されているようです。このケースはここでは自動再生成できません。")
                : T("No stable ID issues can be resolved here.", "ここで解消できる安定IDの問題はありません。");
            return new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = message,
            };
        }

        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, _unfixableDuplicateGroupCount > 0 ? 48f : 0f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        if (_unfixableDuplicateGroupCount > 0)
        {
            panel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = T(
                    "Some duplicate ID groups refer to the same source element across multiple selected views, so they are not listed for regeneration below.",
                    "一部の重複IDグループは、複数の選択ビューにまたがる同一元要素を参照しているため、下の再生成一覧には表示していません。"),
            }, 0, 0);
        }

        _idGrid.Dock = DockStyle.Fill;
        _idGrid.AutoGenerateColumns = false;
        _idGrid.AllowUserToAddRows = false;
        _idGrid.AllowUserToDeleteRows = false;
        _idGrid.AllowUserToResizeRows = false;
        _idGrid.RowHeadersVisible = false;
        _idGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _idGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Regenerate",
            HeaderText = T("Regenerate", "再生成"),
            FillWeight = 10f,
        });
        _idGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "CurrentId",
            HeaderText = T("Current ID", "現在のID"),
            FillWeight = 28f,
            ReadOnly = true,
        });
        _idGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ElementId",
            HeaderText = T("Element ID", "要素ID"),
            FillWeight = 12f,
            ReadOnly = true,
        });
        _idGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FeatureType",
            HeaderText = T("Feature Type", "フィーチャ種別"),
            FillWeight = 12f,
            ReadOnly = true,
        });
        _idGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Views",
            HeaderText = T("Views", "ビュー"),
            FillWeight = 38f,
            ReadOnly = true,
        });

        panel.Controls.Add(_idGrid, 0, 1);
        return panel;
    }

    private void Populate()
    {
        _summaryLabel.Text = T(
            $"Review the suggested fixes below. Missing floor mappings: {_floorCandidates.Count}. Stable IDs to regenerate: {_idCandidates.Count}.",
            $"以下の修正候補を確認してください。未割り当て床マッピング: {_floorCandidates.Count} 件。再生成できる安定ID: {_idCandidates.Count} 件。");

        foreach (FloorAssignmentCandidate candidate in _floorCandidates)
        {
            int rowIndex = _floorGrid.Rows.Add(
                candidate.FloorTypeName,
                candidate.OccurrenceCount,
                candidate.SuggestedCategory ?? string.Empty,
                candidate.SuggestedCategory ?? string.Empty);
            _floorGrid.Rows[rowIndex].Tag = candidate;
        }

        foreach (StableIdCandidate candidate in _idCandidates)
        {
            int rowIndex = _idGrid.Rows.Add(
                true,
                candidate.CurrentId,
                candidate.SourceElementId.ToString(),
                candidate.FeatureType,
                candidate.ViewNames);
            _idGrid.Rows[rowIndex].Tag = candidate;
        }
    }

    private void ApplySelections()
    {
        Dictionary<string, string> floorAssignments = new(StringComparer.Ordinal);
        foreach (DataGridViewRow row in _floorGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            string floorTypeName = row.Cells["FloorTypeName"].Value?.ToString()?.Trim() ?? string.Empty;
            string category = row.Cells["AssignCategory"].Value?.ToString()?.Trim() ?? string.Empty;
            if (floorTypeName.Length == 0 || category.Length == 0)
            {
                continue;
            }

            floorAssignments[floorTypeName] = category;
        }

        List<long> elementIds = new();
        foreach (DataGridViewRow row in _idGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            bool regenerate = row.Cells["Regenerate"].Value as bool? ?? false;
            if (!regenerate || row.Tag is not StableIdCandidate candidate)
            {
                continue;
            }

            elementIds.Add(candidate.SourceElementId);
        }

        SelectedFloorAssignments = floorAssignments;
        SelectedElementIdsToRegenerate = elementIds
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        DialogResult = DialogResult.OK;
        Close();
    }

    private string T(string english, string japanese)
    {
        return UiLanguageText.Select(_language, english, japanese);
    }

    private static IReadOnlyList<FloorAssignmentCandidate> BuildFloorCandidates(
        ExportValidationRequest request,
        ZoneCatalog zoneCatalog,
        IReadOnlyList<string> supportedCategories)
    {
        return request.Views
            .SelectMany(
                view => view.Features
                    .Where(feature => feature.IsUnassigned && !string.IsNullOrWhiteSpace(feature.AssignmentMappingKey))
                    .Select(feature => new
                    {
                        FloorTypeName = feature.AssignmentMappingKey!.Trim(),
                        SuggestedCategory = SuggestCategory(feature.AssignmentMappingKey!, zoneCatalog, supportedCategories),
                    }))
            .GroupBy(item => item.FloorTypeName, StringComparer.Ordinal)
            .Select(group => new FloorAssignmentCandidate(
                group.Key,
                group.Count(),
                group.Select(item => item.SuggestedCategory)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))))
            .OrderBy(candidate => candidate.FloorTypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<StableIdCandidate> BuildStableIdCandidates(
        ExportValidationRequest request,
        out int unfixableDuplicateGroupCount)
    {
        List<StableIdCandidate> candidates = new();
        List<FeatureContext> contexts = request.Views
            .SelectMany(
                view => view.Features.Select(feature => new FeatureContext(view.ViewName, feature)))
            .ToList();

        foreach (IGrouping<long, FeatureContext> group in contexts
                     .Where(context => string.IsNullOrWhiteSpace(context.Feature.ExportId) && context.Feature.SourceElementId.HasValue)
                     .GroupBy(context => context.Feature.SourceElementId!.Value))
        {
            FeatureContext first = group.First();
            candidates.Add(new StableIdCandidate(
                TMany(group.Select(context => context.ViewName)),
                first.Feature.FeatureType,
                string.Empty,
                group.Key));
        }

        unfixableDuplicateGroupCount = 0;
        foreach (IGrouping<string, FeatureContext> group in contexts
                     .Where(context => !string.IsNullOrWhiteSpace(context.Feature.ExportId))
                     .GroupBy(context => context.Feature.ExportId!, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            List<IGrouping<long, FeatureContext>> byElement = group
                .Where(context => context.Feature.SourceElementId.HasValue)
                .GroupBy(context => context.Feature.SourceElementId!.Value)
                .ToList();
            if (byElement.Count <= 1)
            {
                unfixableDuplicateGroupCount++;
                continue;
            }

            foreach (IGrouping<long, FeatureContext> elementGroup in byElement)
            {
                FeatureContext first = elementGroup.First();
                candidates.Add(new StableIdCandidate(
                    TMany(elementGroup.Select(context => context.ViewName)),
                    first.Feature.FeatureType,
                    group.Key,
                    elementGroup.Key));
            }
        }

        return candidates
            .OrderBy(candidate => candidate.CurrentId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.SourceElementId)
            .ToList();
    }

    private static string? SuggestCategory(
        string floorTypeName,
        ZoneCatalog zoneCatalog,
        IReadOnlyList<string> supportedCategories)
    {
        if (TryGetSupportedCategory(floorTypeName, zoneCatalog, supportedCategories, out string? directCategory))
        {
            return directCategory;
        }

        ZoneNameParseResult parsed = ZoneNameParser.Parse(floorTypeName);
        if (parsed.PatternMatched &&
            TryGetSupportedCategory(parsed.ZoneName, zoneCatalog, supportedCategories, out string? parsedCategory))
        {
            return parsedCategory;
        }

        return null;
    }

    private static bool TryGetSupportedCategory(
        string zoneKey,
        ZoneCatalog zoneCatalog,
        IReadOnlyList<string> supportedCategories,
        out string? category)
    {
        category = null;
        if (!zoneCatalog.TryGetZoneInfo(zoneKey, out ZoneInfo zoneInfo))
        {
            return false;
        }

        string value = zoneInfo.Category?.Trim() ?? string.Empty;
        if (value.Length == 0 ||
            !supportedCategories.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        category = supportedCategories
            .First(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    private static string TMany(IEnumerable<string?> values)
    {
        return string.Join(", ", values
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
    }

    private sealed class FloorAssignmentCandidate
    {
        public FloorAssignmentCandidate(string floorTypeName, int occurrenceCount, string? suggestedCategory)
        {
            FloorTypeName = floorTypeName;
            OccurrenceCount = occurrenceCount;
            SuggestedCategory = suggestedCategory;
        }

        public string FloorTypeName { get; }

        public int OccurrenceCount { get; }

        public string? SuggestedCategory { get; }
    }

    private sealed class StableIdCandidate
    {
        public StableIdCandidate(string viewNames, string featureType, string currentId, long sourceElementId)
        {
            ViewNames = viewNames;
            FeatureType = featureType;
            CurrentId = currentId;
            SourceElementId = sourceElementId;
        }

        public string ViewNames { get; }

        public string FeatureType { get; }

        public string CurrentId { get; }

        public long SourceElementId { get; }
    }

    private sealed class FeatureContext
    {
        public FeatureContext(string viewName, ExportFeatureValidationSnapshot feature)
        {
            ViewName = viewName;
            Feature = feature;
        }

        public string ViewName { get; }

        public ExportFeatureValidationSnapshot Feature { get; }
    }
}