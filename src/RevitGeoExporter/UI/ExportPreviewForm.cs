using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Export;
using DrawingColor = System.Drawing.Color;
using WinFormsControl = System.Windows.Forms.Control;
using WinFormsForm = System.Windows.Forms.Form;

namespace RevitGeoExporter.UI;

public sealed class ExportPreviewForm : WinFormsForm
{
    private readonly ExportPreviewRequest _request;
    private readonly ExportPreviewService _previewService;
    private readonly Dictionary<long, PreviewViewData> _cache = new();
    private readonly UiLanguage _language;
    private readonly IReadOnlyList<string> _supportedFloorCategories;

    private readonly ComboBox _viewComboBox = new();
    private readonly CheckBox _unitsCheckBox = new();
    private readonly CheckBox _openingsCheckBox = new();
    private readonly CheckBox _detailsCheckBox = new();
    private readonly CheckBox _levelsCheckBox = new();
    private readonly CheckBox _stairsCheckBox = new();
    private readonly CheckBox _escalatorsCheckBox = new();
    private readonly CheckBox _elevatorsCheckBox = new();
    private readonly CheckBox _warningsOnlyCheckBox = new();
    private readonly CheckBox _overriddenOnlyCheckBox = new();
    private readonly CheckBox _unassignedOnlyCheckBox = new();
    private readonly TextBox _searchTextBox = new();
    private readonly Button _fitButton = new();
    private readonly Button _resetButton = new();
    private readonly Button _closeButton = new();
    private readonly Label _statusLabel = new();
    private readonly PreviewCanvasControl _canvas = new();
    private readonly DataGridView _legendGrid = new();
    private readonly ListBox _warningsList = new();
    private readonly TextBox _detailsTextBox = new();
    private readonly ListBox _unassignedFloorsListBox = new();
    private readonly ComboBox _assignmentCategoryComboBox = new();
    private readonly Button _assignButton = new();
    private readonly Button _clearAssignmentButton = new();
    private readonly Button _saveAssignmentsButton = new();
    private readonly Button _discardAssignmentsButton = new();
    private readonly Label _assignmentTargetValueLabel = new();
    private readonly Label _assignmentCandidateValueLabel = new();
    private readonly Label _assignmentCurrentValueLabel = new();
    private readonly Label _assignmentHintLabel = new();

    private PreviewViewData? _currentViewData;
    private FloorAssignmentTarget? _assignmentTarget;
    private string? _pendingAssignmentFloorTypeName;
    private bool _isLoadingView;
    private bool _suppressUnassignedSelectionChanged;

    public ExportPreviewForm(ExportPreviewRequest request, ExportPreviewService previewService)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _previewService = previewService ?? throw new ArgumentNullException(nameof(previewService));
        _language = request.UiLanguage;
        _supportedFloorCategories = _previewService.GetSupportedFloorCategories().ToList();

        InitializeComponents();
        PopulateAssignmentCategories();
        LoadRequest();
    }

    private void InitializeComponents()
    {
        Text = T("Export Preview", "Export Preview");
        Width = 1400;
        Height = 860;
        MinimumSize = new Size(1120, 720);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        FormClosing += OnFormClosing;

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        Controls.Add(root);

        root.Controls.Add(BuildToolbar(), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);
    }

    private WinFormsControl BuildToolbar()
    {
        TableLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 15,
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        Label viewLabel = new()
        {
            Dock = DockStyle.Fill,
            Text = T("View", "View"),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        toolbar.Controls.Add(viewLabel, 0, 0);

        _viewComboBox.Dock = DockStyle.Fill;
        _viewComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _viewComboBox.SelectedIndexChanged += (_, _) => LoadSelectedView();
        toolbar.Controls.Add(_viewComboBox, 1, 0);

        _unitsCheckBox.Dock = DockStyle.Fill;
        _unitsCheckBox.Text = T("Units", "Units");
        _unitsCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        toolbar.Controls.Add(_unitsCheckBox, 2, 0);

        _openingsCheckBox.Dock = DockStyle.Fill;
        _openingsCheckBox.Text = T("Openings", "Openings");
        _openingsCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        toolbar.Controls.Add(_openingsCheckBox, 3, 0);

        _detailsCheckBox.Dock = DockStyle.Fill;
        _detailsCheckBox.Text = T("Details", "Details");
        _detailsCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        toolbar.Controls.Add(_detailsCheckBox, 4, 0);

        _levelsCheckBox.Dock = DockStyle.Fill;
        _levelsCheckBox.Text = T("Levels", "Levels");
        _levelsCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        toolbar.Controls.Add(_levelsCheckBox, 5, 0);

        _stairsCheckBox.Dock = DockStyle.Fill;
        _stairsCheckBox.Text = T("Stairs", "Stairs");
        _stairsCheckBox.Checked = true;
        _stairsCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        toolbar.Controls.Add(_stairsCheckBox, 6, 0);

        _escalatorsCheckBox.Dock = DockStyle.Fill;
        _escalatorsCheckBox.Text = T("Escalators", "Escalators");
        _escalatorsCheckBox.Checked = true;
        _escalatorsCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        toolbar.Controls.Add(_escalatorsCheckBox, 7, 0);

        _elevatorsCheckBox.Dock = DockStyle.Fill;
        _elevatorsCheckBox.Text = T("Elevators", "Elevators");
        _elevatorsCheckBox.Checked = true;
        _elevatorsCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        toolbar.Controls.Add(_elevatorsCheckBox, 8, 0);

        _warningsOnlyCheckBox.Dock = DockStyle.Fill;
        _warningsOnlyCheckBox.Text = T("Warnings", "Warnings");
        _warningsOnlyCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        toolbar.Controls.Add(_warningsOnlyCheckBox, 9, 0);

        _overriddenOnlyCheckBox.Dock = DockStyle.Fill;
        _overriddenOnlyCheckBox.Text = T("Overrides", "Overrides");
        _overriddenOnlyCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        toolbar.Controls.Add(_overriddenOnlyCheckBox, 10, 0);

        _unassignedOnlyCheckBox.Dock = DockStyle.Fill;
        _unassignedOnlyCheckBox.Text = T("Unassigned", "Unassigned");
        _unassignedOnlyCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        toolbar.Controls.Add(_unassignedOnlyCheckBox, 11, 0);

        _searchTextBox.Dock = DockStyle.Fill;
        _searchTextBox.Text = string.Empty;
        _searchTextBox.TextChanged += (_, _) => ApplyCanvasFilters();
        toolbar.Controls.Add(_searchTextBox, 12, 0);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
        };

        _fitButton.Text = T("Fit", "Fit");
        _fitButton.Width = 72;
        _fitButton.Click += (_, _) => _canvas.FitToFeatures();
        buttons.Controls.Add(_fitButton);

        _resetButton.Text = T("Reset", "Reset");
        _resetButton.Width = 72;
        _resetButton.Click += (_, _) => _canvas.ResetView();
        buttons.Controls.Add(_resetButton);

        toolbar.Controls.Add(buttons, 13, 0);
        return toolbar;
    }

    private WinFormsControl BuildBody()
    {
        SplitContainer split = new()
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 930,
        };

        _canvas.Dock = DockStyle.Fill;
        _canvas.SelectedFeatureChanged += OnSelectedFeatureChanged;
        split.Panel1.Controls.Add(_canvas);

        TabControl tabs = new()
        {
            Dock = DockStyle.Fill,
        };

        TabPage detailsTab = new(T("Details", "Details"));
        TabPage assignmentsTab = new(T("Assignments", "Assignments"));
        TabPage legendTab = new(T("Legend", "Legend"));
        TabPage warningsTab = new(T("Warnings", "Warnings"));

        _detailsTextBox.Dock = DockStyle.Fill;
        _detailsTextBox.Multiline = true;
        _detailsTextBox.ReadOnly = true;
        _detailsTextBox.ScrollBars = ScrollBars.Vertical;
        detailsTab.Controls.Add(_detailsTextBox);

        assignmentsTab.Controls.Add(BuildAssignmentsPanel());

        ConfigureLegendGrid();
        legendTab.Controls.Add(_legendGrid);

        _warningsList.Dock = DockStyle.Fill;
        _warningsList.HorizontalScrollbar = true;
        warningsTab.Controls.Add(_warningsList);

        tabs.TabPages.Add(detailsTab);
        tabs.TabPages.Add(assignmentsTab);
        tabs.TabPages.Add(legendTab);
        tabs.TabPages.Add(warningsTab);
        split.Panel2.Controls.Add(tabs);

        return split;
    }

    private WinFormsControl BuildAssignmentsPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(6),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));

        Label introLabel = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Text = T(
                "Assign project-specific categories to floor-derived units that currently resolve to unspecified.",
                "Assign project-specific categories to floor-derived units that currently resolve to unspecified."),
        };
        panel.Controls.Add(introLabel, 0, 0);

        Label unassignedLabel = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(0, 10, 0, 4),
            Text = T("Unassigned Floors", "Unassigned Floors"),
        };
        panel.Controls.Add(unassignedLabel, 0, 1);

        _unassignedFloorsListBox.Dock = DockStyle.Fill;
        _unassignedFloorsListBox.SelectionMode = SelectionMode.MultiExtended;
        _unassignedFloorsListBox.SelectedIndexChanged += (_, _) => OnUnassignedFloorSelectionChanged();
        panel.Controls.Add(_unassignedFloorsListBox, 0, 2);

        panel.Controls.Add(BuildAssignmentValueRow(T("Floor Type", "Floor Type"), _assignmentTargetValueLabel), 0, 3);
        panel.Controls.Add(BuildAssignmentValueRow(T("Parsed Candidate", "Parsed Candidate"), _assignmentCandidateValueLabel), 0, 4);
        panel.Controls.Add(BuildAssignmentValueRow(T("Current Resolution", "Current Resolution"), _assignmentCurrentValueLabel), 0, 5);

        FlowLayoutPanel assignmentActions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0),
        };

        _assignmentCategoryComboBox.Width = 190;
        _assignmentCategoryComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        assignmentActions.Controls.Add(_assignmentCategoryComboBox);

        _assignButton.Text = T("Assign", "Assign");
        _assignButton.AutoSize = true;
        _assignButton.Click += (_, _) => AssignSelectedFloorCategory();
        assignmentActions.Controls.Add(_assignButton);

        _clearAssignmentButton.Text = T("Clear Override", "Clear Override");
        _clearAssignmentButton.AutoSize = true;
        _clearAssignmentButton.Click += (_, _) => ClearSelectedFloorCategoryOverride();
        assignmentActions.Controls.Add(_clearAssignmentButton);

        panel.Controls.Add(assignmentActions, 0, 6);

        FlowLayoutPanel persistenceActions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0),
        };

        _saveAssignmentsButton.Text = T("Save Assignments", "Save Assignments");
        _saveAssignmentsButton.AutoSize = true;
        _saveAssignmentsButton.Click += (_, _) => SavePendingAssignments();
        persistenceActions.Controls.Add(_saveAssignmentsButton);

        _discardAssignmentsButton.Text = T("Discard Pending", "Discard Pending");
        _discardAssignmentsButton.AutoSize = true;
        _discardAssignmentsButton.Click += (_, _) => DiscardPendingAssignments();
        persistenceActions.Controls.Add(_discardAssignmentsButton);

        panel.Controls.Add(persistenceActions, 0, 7);

        _assignmentHintLabel.Dock = DockStyle.Fill;
        _assignmentHintLabel.AutoSize = true;
        _assignmentHintLabel.Padding = new Padding(0, 10, 0, 0);
        panel.Controls.Add(_assignmentHintLabel, 0, 8);

        UpdateAssignmentControls();
        return panel;
    }

    private static WinFormsControl BuildAssignmentValueRow(string labelText, Label valueLabel)
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        Label label = new()
        {
            Dock = DockStyle.Fill,
            Text = labelText,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 6, 0, 0),
        };
        row.Controls.Add(label, 0, 0);

        valueLabel.Dock = DockStyle.Fill;
        valueLabel.AutoEllipsis = true;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        valueLabel.Padding = new Padding(0, 6, 0, 0);
        row.Controls.Add(valueLabel, 1, 0);

        return row;
    }

    private WinFormsControl BuildFooter()
    {
        TableLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Text = T(
            "Mouse wheel zooms. Drag to pan. Click a feature to inspect it.",
            "Mouse wheel zooms. Drag to pan. Click a feature to inspect it.");
        footer.Controls.Add(_statusLabel, 0, 0);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
        };

        _closeButton.Text = T("Close", "Close");
        _closeButton.Width = 88;
        _closeButton.DialogResult = DialogResult.OK;
        actions.Controls.Add(_closeButton);

        footer.Controls.Add(actions, 1, 0);
        AcceptButton = _closeButton;
        CancelButton = _closeButton;

        return footer;
    }

    private void ConfigureLegendGrid()
    {
        _legendGrid.Dock = DockStyle.Fill;
        _legendGrid.ReadOnly = true;
        _legendGrid.MultiSelect = false;
        _legendGrid.RowHeadersVisible = false;
        _legendGrid.AllowUserToAddRows = false;
        _legendGrid.AllowUserToDeleteRows = false;
        _legendGrid.AllowUserToResizeRows = false;
        _legendGrid.AllowUserToResizeColumns = false;
        _legendGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _legendGrid.AutoGenerateColumns = false;
        _legendGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _legendGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Color",
            HeaderText = string.Empty,
            FillWeight = 12f,
        });
        _legendGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Category",
            HeaderText = T("Category", "Category"),
            FillWeight = 58f,
        });
        _legendGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Count",
            HeaderText = T("Count", "Count"),
            FillWeight = 30f,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleRight,
            },
        });
    }

    private void PopulateAssignmentCategories()
    {
        _assignmentCategoryComboBox.Items.Clear();
        foreach (string category in _supportedFloorCategories)
        {
            _assignmentCategoryComboBox.Items.Add(category);
        }

        if (_assignmentCategoryComboBox.Items.Count > 0)
        {
            _assignmentCategoryComboBox.SelectedIndex = 0;
        }
    }

    private void LoadRequest()
    {
        _unitsCheckBox.Checked = _request.FeatureTypes.HasFlag(ExportFeatureType.Unit);
        _openingsCheckBox.Checked = _request.FeatureTypes.HasFlag(ExportFeatureType.Opening);
        _detailsCheckBox.Checked = _request.FeatureTypes.HasFlag(ExportFeatureType.Detail);
        _levelsCheckBox.Checked = _request.FeatureTypes.HasFlag(ExportFeatureType.Level);

        foreach (ViewPlan view in _request.SelectedViews)
        {
            _viewComboBox.Items.Add(new ViewItem(view, _language));
        }

        if (_viewComboBox.Items.Count > 0)
        {
            _viewComboBox.SelectedIndex = 0;
        }
    }

    private void LoadSelectedView()
    {
        if (_viewComboBox.SelectedItem is not ViewItem viewItem)
        {
            return;
        }

        PreviewViewData preview;
        bool loadedFromCache = _cache.TryGetValue(viewItem.View.Id.Value, out preview!);
        if (!loadedFromCache)
        {
            UseWaitCursor = true;
            Cursor.Current = Cursors.WaitCursor;
            _statusLabel.Text = T(
                $"Loading preview for {viewItem.View.Name}...",
                $"Loading preview for {viewItem.View.Name}...");
            Refresh();

            preview = _previewService.PrepareView(viewItem.View, _request.FeatureTypes);
            _cache[viewItem.View.Id.Value] = preview;
        }

        _isLoadingView = true;
        try
        {
            _currentViewData = preview;
            _canvas.SetViewData(preview.Features, preview.Bounds);
            ApplyCanvasFilters();
            PopulateLegend(preview);
            PopulateWarnings(preview);
            PopulateUnassignedFloors(preview);
            UpdateDetails(null);
            RestoreAssignmentTargetAfterReload();
            UpdateStatus(preview);
        }
        finally
        {
            _isLoadingView = false;
            UseWaitCursor = false;
        }
    }

    private void ApplyCanvasFilters()
    {
        _canvas.ShowUnits = _unitsCheckBox.Checked;
        _canvas.ShowOpenings = _openingsCheckBox.Checked;
        _canvas.ShowDetails = _detailsCheckBox.Checked;
        _canvas.ShowLevels = _levelsCheckBox.Checked;
        _canvas.ShowStairs = _stairsCheckBox.Checked;
        _canvas.ShowEscalators = _escalatorsCheckBox.Checked;
        _canvas.ShowElevators = _elevatorsCheckBox.Checked;
        _canvas.ShowWarningsOnly = _warningsOnlyCheckBox.Checked;
        _canvas.ShowOverriddenOnly = _overriddenOnlyCheckBox.Checked;
        _canvas.ShowUnassignedOnly = _unassignedOnlyCheckBox.Checked;
        _canvas.SearchText = _searchTextBox.Text;
        _canvas.RefreshFilters();
    }

    private void PopulateLegend(PreviewViewData viewData)
    {
        _legendGrid.Rows.Clear();

        List<PreviewFeatureData> legendFeatures = viewData.Features
            .Where(feature => feature.FeatureType == ExportFeatureType.Unit)
            .ToList();

        var grouped = legendFeatures
            .GroupBy(
                feature => (feature.Category ?? string.Empty).Trim(),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, PreviewFeatureData> group in grouped)
        {
            PreviewFeatureData first = group.First();
            int rowIndex = _legendGrid.Rows.Add(string.Empty, GetLegendLabel(group.Key), group.Count());
            _legendGrid.Rows[rowIndex].Cells[0].Style.BackColor =
                ParseColor(first.FillColorHex, DrawingColor.LightGray);
        }
    }

    private void PopulateWarnings(PreviewViewData viewData)
    {
        _warningsList.Items.Clear();
        if (viewData.Warnings.Count == 0)
        {
            _warningsList.Items.Add(T("No warnings.", "No warnings."));
            return;
        }

        foreach (string warning in viewData.Warnings)
        {
            _warningsList.Items.Add(warning);
        }
    }

    private void PopulateUnassignedFloors(PreviewViewData viewData)
    {
        string? selectedFloorType = _assignmentTarget?.FloorTypeName ?? _pendingAssignmentFloorTypeName;
        _suppressUnassignedSelectionChanged = true;
        try
        {
            _unassignedFloorsListBox.Items.Clear();
            foreach (PreviewUnassignedFloorGroup group in viewData.UnassignedFloors)
            {
                _unassignedFloorsListBox.Items.Add(new UnassignedFloorItem(group));
            }

            SelectUnassignedFloorItem(selectedFloorType);
        }
        finally
        {
            _suppressUnassignedSelectionChanged = false;
        }
    }

    private void UpdateDetails(PreviewFeatureData? feature)
    {
        if (_currentViewData == null)
        {
            _detailsTextBox.Text = string.Empty;
            return;
        }

        if (feature == null)
        {
            _detailsTextBox.Text =
                $"View: {_currentViewData.ViewName}{Environment.NewLine}" +
                $"Level: {_currentViewData.LevelName}{Environment.NewLine}" +
                $"Features: {_currentViewData.Features.Count}{Environment.NewLine}" +
                $"Unassigned Floor Types: {_currentViewData.UnassignedFloors.Count}{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                T(
                    "Click a feature to inspect its metadata.",
                    "Click a feature to inspect its metadata.");
            return;
        }

        List<string> lines = new()
        {
            $"Feature Type: {feature.FeatureType}",
            $"Category: {NullToPlaceholder(feature.Category)}",
            $"Name: {NullToPlaceholder(feature.Name)}",
            $"Restriction: {NullToPlaceholder(feature.Restriction)}",
            $"Export ID: {NullToPlaceholder(feature.ExportId)}",
            $"Source Element ID: {(feature.SourceElementId.HasValue ? feature.SourceElementId.Value.ToString() : "-")}",
            $"Fill Color: #{feature.FillColorHex}",
            $"Stroke Color: #{feature.StrokeColorHex}",
        };

        if (feature.IsFloorDerived)
        {
            lines.Add($"Floor Type: {NullToPlaceholder(feature.FloorTypeName)}");
            lines.Add($"Parsed Candidate: {NullToPlaceholder(feature.ParsedZoneCandidate)}");
            lines.Add($"Category Resolution: {FormatResolutionSource(feature.CategoryResolutionSource)}");
            lines.Add($"Unassigned Floor: {(feature.IsUnassignedFloor ? "Yes" : "No")}");
        }

        _detailsTextBox.Text = string.Join(Environment.NewLine, lines);
    }

    private void OnSelectedFeatureChanged(PreviewFeatureData? feature)
    {
        if (_isLoadingView && feature == null)
        {
            return;
        }

        UpdateDetails(feature);

        if (feature == null)
        {
            if (!_isLoadingView)
            {
                SetAssignmentTarget(null, selectMatchingListItem: false);
                SelectUnassignedFloorItem(null);
            }

            return;
        }

        if (!feature.SupportsFloorCategoryAssignment)
        {
            SetAssignmentTarget(null, selectMatchingListItem: false);
            SelectUnassignedFloorItem(null);
            return;
        }

        SetAssignmentTarget(CreateAssignmentTarget(feature), selectMatchingListItem: true);
    }

    private void OnUnassignedFloorSelectionChanged()
    {
        if (_suppressUnassignedSelectionChanged)
        {
            return;
        }

        if (_unassignedFloorsListBox.SelectedItem is not UnassignedFloorItem item)
        {
            return;
        }

        FloorAssignmentTarget target = TryBuildAssignmentTarget(item.Group.FloorTypeName)
            ?? new FloorAssignmentTarget(
                item.Group.FloorTypeName,
                item.Group.ParsedZoneCandidate,
                currentCategory: "unspecified",
                hasOverride: false,
                isUnassigned: true);
        SetAssignmentTarget(target, selectMatchingListItem: false);
    }

    private void AssignSelectedFloorCategory()
    {
        if (_assignmentTarget == null ||
            _assignmentCategoryComboBox.SelectedItem is not string category ||
            string.IsNullOrWhiteSpace(category))
        {
            return;
        }

        IReadOnlyList<string> floorTypeNames = GetSelectedFloorTypeNames();
        if (floorTypeNames.Count == 0)
        {
            return;
        }

        foreach (string floorTypeName in floorTypeNames)
        {
            _previewService.StageFloorCategoryOverride(floorTypeName, category);
        }

        _pendingAssignmentFloorTypeName = floorTypeNames[0];
        _cache.Clear();
        LoadSelectedView();
        _statusLabel.Text = T(
            $"Pending floor override for {floorTypeNames.Count} floor type(s) -> {category}. Save assignments to persist it.",
            $"Pending floor override for {floorTypeNames.Count} floor type(s) -> {category}. Save assignments to persist it.");
    }

    private void ClearSelectedFloorCategoryOverride()
    {
        IReadOnlyList<string> floorTypeNames = GetSelectedFloorTypeNames();
        if (floorTypeNames.Count == 0)
        {
            return;
        }

        foreach (string floorTypeName in floorTypeNames)
        {
            _previewService.StageClearFloorCategoryOverride(floorTypeName);
        }

        _pendingAssignmentFloorTypeName = floorTypeNames[0];
        _cache.Clear();
        LoadSelectedView();
        _statusLabel.Text = T(
            $"Pending override removal for {floorTypeNames.Count} floor type(s). Save assignments to persist it.",
            $"Pending override removal for {floorTypeNames.Count} floor type(s). Save assignments to persist it.");
    }

    private void SavePendingAssignments()
    {
        if (!_previewService.HasPendingFloorCategoryChanges)
        {
            return;
        }

        _previewService.ApplyPendingFloorCategoryOverrides();
        _cache.Clear();
        LoadSelectedView();
        _statusLabel.Text = T(
            "Saved preview floor assignments.",
            "Saved preview floor assignments.");
    }

    private void DiscardPendingAssignments()
    {
        if (!_previewService.HasPendingFloorCategoryChanges)
        {
            return;
        }

        _previewService.DiscardPendingFloorCategoryOverrides();
        _cache.Clear();
        LoadSelectedView();
        _statusLabel.Text = T(
            "Discarded pending preview floor assignments.",
            "Discarded pending preview floor assignments.");
    }

    private void RestoreAssignmentTargetAfterReload()
    {
        string? pendingFloorTypeName = _pendingAssignmentFloorTypeName;
        _pendingAssignmentFloorTypeName = null;

        if (string.IsNullOrWhiteSpace(pendingFloorTypeName))
        {
            SetAssignmentTarget(null, selectMatchingListItem: false);
            return;
        }

        FloorAssignmentTarget? target = TryBuildAssignmentTarget(pendingFloorTypeName!);
        SetAssignmentTarget(target, selectMatchingListItem: true);
    }

    private FloorAssignmentTarget CreateAssignmentTarget(PreviewFeatureData feature)
    {
        return new FloorAssignmentTarget(
            feature.FloorTypeName ?? string.Empty,
            feature.ParsedZoneCandidate,
            feature.Category,
            feature.UsesFloorCategoryOverride,
            feature.IsUnassignedFloor);
    }

    private FloorAssignmentTarget? TryBuildAssignmentTarget(string floorTypeName)
    {
        if (_currentViewData == null || string.IsNullOrWhiteSpace(floorTypeName))
        {
            return null;
        }

        PreviewFeatureData? feature = _currentViewData.Features
            .Where(candidate => candidate.IsFloorDerived &&
                                string.Equals(candidate.FloorTypeName, floorTypeName, StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.SupportsFloorCategoryAssignment)
            .FirstOrDefault();

        return feature == null ? null : CreateAssignmentTarget(feature);
    }

    private void SetAssignmentTarget(FloorAssignmentTarget? target, bool selectMatchingListItem)
    {
        _assignmentTarget = target;
        UpdateAssignmentControls();
        if (selectMatchingListItem)
        {
            SelectUnassignedFloorItem(target?.FloorTypeName);
        }
    }

    private void UpdateAssignmentControls()
    {
        if (_assignmentTarget == null)
        {
            _assignmentTargetValueLabel.Text = "-";
            _assignmentCandidateValueLabel.Text = "-";
            _assignmentCurrentValueLabel.Text = "-";
            _assignmentCategoryComboBox.Enabled = false;
            _assignButton.Enabled = false;
            _clearAssignmentButton.Enabled = false;
            _saveAssignmentsButton.Enabled = _previewService.HasPendingFloorCategoryChanges;
            _discardAssignmentsButton.Enabled = _previewService.HasPendingFloorCategoryChanges;
            _assignmentHintLabel.Text = T(
                "Select one or more floor types from the list, or click a floor-derived unit on the canvas, to stage category changes. Use Save Assignments to persist pending changes.",
                "Select one or more floor types from the list, or click a floor-derived unit on the canvas, to stage category changes. Use Save Assignments to persist pending changes.");
            return;
        }

        _assignmentTargetValueLabel.Text = NullToPlaceholder(_assignmentTarget.FloorTypeName);
        _assignmentCandidateValueLabel.Text = NullToPlaceholder(_assignmentTarget.ParsedZoneCandidate);
        _assignmentCurrentValueLabel.Text = BuildAssignmentSummary(_assignmentTarget);
        _assignmentCategoryComboBox.Enabled = _assignmentCategoryComboBox.Items.Count > 0;
        SelectAssignmentCategory(_assignmentTarget.CurrentCategory);
        _assignButton.Enabled = _assignmentCategoryComboBox.Enabled;
        _clearAssignmentButton.Enabled = _assignmentTarget.HasOverride;
        _saveAssignmentsButton.Enabled = _previewService.HasPendingFloorCategoryChanges;
        _discardAssignmentsButton.Enabled = _previewService.HasPendingFloorCategoryChanges;
        _assignmentHintLabel.Text = _assignmentTarget.HasOverride
            ? T(
                "This floor type currently uses a saved override. You can batch-assign or clear multiple selected floor types, then save the staged changes.",
                "This floor type currently uses a saved override. You can batch-assign or clear multiple selected floor types, then save the staged changes.")
            : T(
                "Assigning a category stages a project-specific override for the selected floor type(s) without changing the Revit model. Save Assignments to persist it.",
                "Assigning a category stages a project-specific override for the selected floor type(s) without changing the Revit model. Save Assignments to persist it.");
    }

    private void SelectAssignmentCategory(string? category)
    {
        if (_assignmentCategoryComboBox.Items.Count == 0)
        {
            return;
        }

        string trimmedCategory = (category ?? string.Empty).Trim();
        for (int i = 0; i < _assignmentCategoryComboBox.Items.Count; i++)
        {
            if (string.Equals(
                    _assignmentCategoryComboBox.Items[i]?.ToString(),
                    trimmedCategory,
                    StringComparison.OrdinalIgnoreCase))
            {
                _assignmentCategoryComboBox.SelectedIndex = i;
                return;
            }
        }

        if (_assignmentCategoryComboBox.SelectedIndex < 0)
        {
            _assignmentCategoryComboBox.SelectedIndex = 0;
        }
    }

    private void SelectUnassignedFloorItem(string? floorTypeName)
    {
        _suppressUnassignedSelectionChanged = true;
        try
        {
            if (string.IsNullOrWhiteSpace(floorTypeName))
            {
                _unassignedFloorsListBox.ClearSelected();
                return;
            }

            for (int i = 0; i < _unassignedFloorsListBox.Items.Count; i++)
            {
                if (_unassignedFloorsListBox.Items[i] is UnassignedFloorItem item &&
                    string.Equals(item.Group.FloorTypeName, floorTypeName, StringComparison.Ordinal))
                {
                    _unassignedFloorsListBox.SelectedIndex = i;
                    return;
                }
            }

            _unassignedFloorsListBox.ClearSelected();
        }
        finally
        {
            _suppressUnassignedSelectionChanged = false;
        }
    }

    private IReadOnlyList<string> GetSelectedFloorTypeNames()
    {
        List<string> names = _unassignedFloorsListBox.SelectedItems
            .OfType<UnassignedFloorItem>()
            .Select(item => item.Group.FloorTypeName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (names.Count == 0 && _assignmentTarget != null && !string.IsNullOrWhiteSpace(_assignmentTarget.FloorTypeName))
        {
            names.Add(_assignmentTarget.FloorTypeName);
        }

        return names;
    }

    private void UpdateStatus(PreviewViewData preview)
    {
        string suffix = _previewService.HasPendingFloorCategoryChanges
            ? T(" | unsaved assignment changes", " | unsaved assignment changes")
            : string.Empty;
        _statusLabel.Text = T(
            $"{preview.ViewName} [{preview.LevelName}] - {preview.Features.Count} preview features, {preview.UnassignedFloors.Count} unassigned floor types, {preview.AvailableSourceLabels.Count} source labels",
            $"{preview.ViewName} [{preview.LevelName}] - {preview.Features.Count} preview features, {preview.UnassignedFloors.Count} unassigned floor types, {preview.AvailableSourceLabels.Count} source labels") + suffix;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_previewService.HasPendingFloorCategoryChanges)
        {
            _previewService.DiscardPendingFloorCategoryOverrides();
        }
    }

    private static string BuildAssignmentSummary(FloorAssignmentTarget target)
    {
        string category = NullToPlaceholder(target.CurrentCategory);
        string source = target.HasOverride
            ? "Override"
            : target.IsUnassigned
                ? "Fallback"
                : "Catalog";
        return $"{category} ({source})";
    }

    private static string FormatResolutionSource(FloorCategoryResolutionSource? resolutionSource)
    {
        return resolutionSource switch
        {
            FloorCategoryResolutionSource.Catalog => "Catalog",
            FloorCategoryResolutionSource.Override => "Override",
            FloorCategoryResolutionSource.FallbackUnspecified => "Fallback",
            _ => "-",
        };
    }

    private static string GetLegendLabel(string category)
    {
        return string.IsNullOrWhiteSpace(category) ? "(uncategorized)" : category;
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

    private static DrawingColor ParseColor(string hex, DrawingColor fallback)
    {
        string normalized = (hex ?? string.Empty).Trim().TrimStart('#');
        if (normalized.Length != 6)
        {
            return fallback;
        }

        try
        {
            int r = Convert.ToInt32(normalized.Substring(0, 2), 16);
            int g = Convert.ToInt32(normalized.Substring(2, 2), 16);
            int b = Convert.ToInt32(normalized.Substring(4, 2), 16);
            return DrawingColor.FromArgb(r, g, b);
        }
        catch
        {
            return fallback;
        }
    }

    private string T(string english, string japanese)
    {
        return UiLanguageText.Select(_language, english, japanese);
    }

    private sealed class ViewItem
    {
        public ViewItem(ViewPlan view, UiLanguage language)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
            Language = language;
        }

        public ViewPlan View { get; }

        public UiLanguage Language { get; }

        public override string ToString()
        {
            string levelName = View.GenLevel?.Name ?? "<no level>";
            string levelLabel = Language == UiLanguage.Japanese ? "Level" : "Level";
            return $"{View.Name}  [{levelLabel}: {levelName}]";
        }
    }

    private sealed class UnassignedFloorItem
    {
        public UnassignedFloorItem(PreviewUnassignedFloorGroup group)
        {
            Group = group ?? throw new ArgumentNullException(nameof(group));
        }

        public PreviewUnassignedFloorGroup Group { get; }

        public override string ToString()
        {
            return $"{Group.FloorTypeName} ({Group.UnitCount})";
        }
    }

    private sealed class FloorAssignmentTarget
    {
        public FloorAssignmentTarget(
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
