using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Help;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Export;
using RevitGeoExporter.Resources;
using DrawingColor = System.Drawing.Color;
using WinFormsControl = System.Windows.Forms.Control;
using WinFormsForm = System.Windows.Forms.Form;

namespace RevitGeoExporter.UI;

public sealed class ExportPreviewForm : WinFormsForm
{
    private readonly ExportPreviewRequest _request;
    private readonly ExportPreviewService _previewService;
    private readonly ExportPreviewController _controller;
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
    private readonly CheckBox _basemapCheckBox = new();
    private readonly CheckBox _surveyPointCheckBox = new();
    private readonly TextBox _searchTextBox = new();
    private readonly Button _fitButton = new();
    private readonly Button _resetButton = new();
    private readonly Button _closeButton = new();
    private readonly Button _helpButton = new();
    private readonly Label _statusLabel = new();
    private readonly PreviewCanvasControl _canvas = new();
    private readonly FlowLayoutPanel _legendPanel = new();
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
    private SplitContainer? _bodyLayoutSplit;
    private SplitContainer? _workspaceSplit;

    private PreviewViewData? _currentViewData;
    private PreviewDisplayViewState? _currentDisplayState;
    private string _statusMessage = string.Empty;
    private string _basemapProviderStatus = string.Empty;
    private bool _isLoadingView;
    private bool _suppressUnassignedSelectionChanged;

    public ExportPreviewForm(ExportPreviewRequest request, ExportPreviewService previewService)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _previewService = previewService ?? throw new ArgumentNullException(nameof(previewService));
        _controller = new ExportPreviewController(request, previewService);
        _language = request.UiLanguage;
        _supportedFloorCategories = _controller.SupportedFloorCategories.ToList();
        _canvas.BasemapStatusChanged += message =>
        {
            _controller.UpdateBasemapProviderStatus(message);
            RefreshStatusText();
        };

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
        Shown += (_, _) => BeginInvoke(new Action(ApplyPreferredSplitLayout));

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
        SetStatusMessage(T("Mouse wheel zooms. Drag to pan. Click a feature to inspect it.", "Mouse wheel zooms. Drag to pan. Click a feature to inspect it."));
    }

    private WinFormsControl BuildToolbar()
    {
        TableLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56f));
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
        return toolbar;
    }

    private WinFormsControl BuildBody()
    {
        SplitContainer layout = new()
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
        };
        _bodyLayoutSplit = layout;
        layout.Panel1.Padding = new Padding(0, 0, 10, 0);
        layout.Panel1.Controls.Add(BuildSidebar());
        layout.Panel2.Controls.Add(BuildWorkspace());

        return layout;
    }

    private WinFormsControl BuildSidebar()
    {
        TableLayoutPanel sidebar = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            AutoScroll = true,
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _searchTextBox.Width = 178;
        _searchTextBox.Text = string.Empty;
        _searchTextBox.TextChanged += (_, _) => ApplyCanvasFilters();
        sidebar.Controls.Add(BuildSearchGroup(), 0, 0);

        ConfigureSidebarCheckBox(_unitsCheckBox, T("Units", "Units"));
        _unitsCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        ConfigureSidebarCheckBox(_openingsCheckBox, T("Openings", "Openings"));
        _openingsCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        ConfigureSidebarCheckBox(_detailsCheckBox, T("Details", "Details"));
        _detailsCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        ConfigureSidebarCheckBox(_levelsCheckBox, T("Levels", "Levels"));
        _levelsCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        ConfigureSidebarCheckBox(_stairsCheckBox, T("Stairs", "Stairs"));
        _stairsCheckBox.Checked = true;
        _stairsCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        ConfigureSidebarCheckBox(_escalatorsCheckBox, T("Escalators", "Escalators"));
        _escalatorsCheckBox.Checked = true;
        _escalatorsCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        ConfigureSidebarCheckBox(_elevatorsCheckBox, T("Elevators", "Elevators"));
        _elevatorsCheckBox.Checked = true;
        _elevatorsCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        ConfigureSidebarCheckBox(_basemapCheckBox, L("Preview.ShowBasemap", "Show basemap"));
        _basemapCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        ConfigureSidebarCheckBox(_surveyPointCheckBox, L("Preview.ShowSurveyPoint", "Show survey point"));
        _surveyPointCheckBox.CheckedChanged += (_, _) =>
        {
            ApplyCanvasFilters();
            if (_surveyPointCheckBox.Checked && _surveyPointCheckBox.Enabled)
            {
                _canvas.FitToFeatures();
            }
        };
        sidebar.Controls.Add(
            BuildSidebarGroup(
                T("Map Layers", "Map Layers"),
                _unitsCheckBox,
                _openingsCheckBox,
                _detailsCheckBox,
                _levelsCheckBox,
                _stairsCheckBox,
                _escalatorsCheckBox,
                _elevatorsCheckBox,
                _basemapCheckBox,
                _surveyPointCheckBox),
            0,
            1);

        ConfigureSidebarCheckBox(_warningsOnlyCheckBox, T("Warnings only", "Warnings only"));
        _warningsOnlyCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        ConfigureSidebarCheckBox(_overriddenOnlyCheckBox, T("Overrides only", "Overrides only"));
        _overriddenOnlyCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        ConfigureSidebarCheckBox(_unassignedOnlyCheckBox, T("Unassigned only", "Unassigned only"));
        _unassignedOnlyCheckBox.CheckedChanged += (_, _) => ApplyCanvasFilters();
        sidebar.Controls.Add(
            BuildSidebarGroup(
                T("Filters", "Filters"),
                _warningsOnlyCheckBox,
                _overriddenOnlyCheckBox,
                _unassignedOnlyCheckBox),
            0,
            2);

        _fitButton.Text = T("Fit", "Fit");
        _fitButton.Width = 78;
        _fitButton.Click += (_, _) => _canvas.FitToFeatures();
        _resetButton.Text = T("Reset", "Reset");
        _resetButton.Width = 78;
        _resetButton.Click += (_, _) => _canvas.ResetView();
        sidebar.Controls.Add(BuildViewToolsGroup(), 0, 3);

        return sidebar;
    }

    private WinFormsControl BuildWorkspace()
    {
        SplitContainer split = new()
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2,
        };
        _workspaceSplit = split;

        split.Panel1.Controls.Add(BuildMapWorkspace());
        split.Panel2.Controls.Add(BuildInspectorTabs());
        return split;
    }

    private void ApplyPreferredSplitLayout()
    {
        TryConfigureSplitContainer(_bodyLayoutSplit, panel1MinSize: 210, panel2MinSize: 720, preferredDistance: 230);
        TryConfigureSplitContainer(_workspaceSplit, panel1MinSize: 420, panel2MinSize: 260, preferredDistance: 790);
    }

    private static void TryConfigureSplitContainer(
        SplitContainer? splitContainer,
        int panel1MinSize,
        int panel2MinSize,
        int preferredDistance)
    {
        if (splitContainer is null || splitContainer.IsDisposed || !splitContainer.IsHandleCreated)
        {
            return;
        }

        int totalSize = splitContainer.Orientation == Orientation.Horizontal
            ? splitContainer.ClientSize.Height
            : splitContainer.ClientSize.Width;
        if (totalSize <= 0)
        {
            return;
        }

        int maxDistance = totalSize - panel2MinSize - splitContainer.SplitterWidth;
        if (maxDistance < panel1MinSize)
        {
            return;
        }

        int safeDistance = Math.Max(panel1MinSize, Math.Min(preferredDistance, maxDistance));
        try
        {
            splitContainer.Panel1MinSize = 0;
            splitContainer.Panel2MinSize = 0;
            splitContainer.SplitterDistance = safeDistance;
            splitContainer.Panel1MinSize = panel1MinSize;
            splitContainer.Panel2MinSize = panel2MinSize;
        }
        catch (ArgumentOutOfRangeException)
        {
            // If WinForms still reports an intermediate invalid size, leave the default split in place.
        }
    }

    private WinFormsControl BuildMapWorkspace()
    {
        TableLayoutPanel workspace = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        workspace.Controls.Add(BuildLegendStrip(), 0, 0);
        _canvas.Dock = DockStyle.Fill;
        _canvas.SelectedFeatureChanged += OnSelectedFeatureChanged;
        workspace.Controls.Add(_canvas, 0, 1);

        return workspace;
    }

    private WinFormsControl BuildInspectorTabs()
    {
        TabControl tabs = new()
        {
            Dock = DockStyle.Fill,
        };

        TabPage detailsTab = new(T("Details", "Details"));
        TabPage assignmentsTab = new(T("Assignments", "Assignments"));
        TabPage warningsTab = new(T("Warnings", "Warnings"));

        _detailsTextBox.Dock = DockStyle.Fill;
        _detailsTextBox.Multiline = true;
        _detailsTextBox.ReadOnly = true;
        _detailsTextBox.ScrollBars = ScrollBars.Vertical;
        detailsTab.Controls.Add(_detailsTextBox);

        assignmentsTab.Controls.Add(BuildAssignmentsPanel());

        _warningsList.Dock = DockStyle.Fill;
        _warningsList.HorizontalScrollbar = true;
        warningsTab.Controls.Add(_warningsList);

        tabs.TabPages.Add(detailsTab);
        tabs.TabPages.Add(assignmentsTab);
        tabs.TabPages.Add(warningsTab);
        return tabs;
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
        _statusLabel.Text = string.Empty;
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
        _closeButton.Click += (_, _) => Close();
        actions.Controls.Add(_closeButton);

        _helpButton.Text = L("Common.Help", "Help");
        _helpButton.Width = 88;
        _helpButton.Click += (_, _) => HelpLauncher.Show(this, HelpTopic.PreviewAndAssignments, _language, Text);
        actions.Controls.Add(_helpButton);

        footer.Controls.Add(actions, 1, 0);
        CancelButton = _closeButton;

        return footer;
    }

    private WinFormsControl BuildSearchGroup()
    {
        GroupBox group = CreateSidebarGroup(T("Search", "Search"));

        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
        };
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = T("Name, category, or export ID", "Name, category, or export ID"),
            Margin = new Padding(0, 0, 0, 4),
        }, 0, 0);
        panel.Controls.Add(_searchTextBox, 0, 1);

        group.Controls.Add(panel);
        return group;
    }

    private WinFormsControl BuildViewToolsGroup()
    {
        GroupBox group = CreateSidebarGroup(T("View Tools", "View Tools"));

        FlowLayoutPanel panel = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
        };
        panel.Controls.Add(_fitButton);
        panel.Controls.Add(_resetButton);

        group.Controls.Add(panel);
        return group;
    }

    private WinFormsControl BuildSidebarGroup(string title, params WinFormsControl[] controls)
    {
        GroupBox group = CreateSidebarGroup(title);

        FlowLayoutPanel panel = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
        };

        foreach (WinFormsControl control in controls)
        {
            panel.Controls.Add(control);
        }

        group.Controls.Add(panel);
        return group;
    }

    private WinFormsControl BuildLegendStrip()
    {
        System.Windows.Forms.Panel container = new()
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(10, 8, 10, 6),
            BackColor = DrawingColor.FromArgb(245, 245, 245),
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = T("Legend", "Legend"),
            Font = new Font(Font, FontStyle.Bold),
            Margin = Padding.Empty,
        }, 0, 0);

        _legendPanel.Dock = DockStyle.Top;
        _legendPanel.AutoSize = true;
        _legendPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _legendPanel.FlowDirection = FlowDirection.LeftToRight;
        _legendPanel.WrapContents = true;
        _legendPanel.Margin = Padding.Empty;
        _legendPanel.Padding = new Padding(0, 6, 0, 0);
        layout.Controls.Add(_legendPanel, 0, 1);

        container.Controls.Add(layout);
        return container;
    }

    private static GroupBox CreateSidebarGroup(string title)
    {
        return new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = title,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(10, 6, 10, 10),
        };
    }

    private static void ConfigureSidebarCheckBox(CheckBox checkBox, string text)
    {
        checkBox.AutoSize = true;
        checkBox.Text = text;
        checkBox.Margin = new Padding(0, 0, 0, 6);
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
        _basemapCheckBox.Checked = false;
        _basemapCheckBox.Enabled = false;
        _surveyPointCheckBox.Checked = false;
        _surveyPointCheckBox.Enabled = false;

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

        if (!_controller.IsViewCached(viewItem.View))
        {
            UseWaitCursor = true;
            Cursor.Current = Cursors.WaitCursor;
            SetStatusMessage(_controller.BuildLoadingStatus(viewItem.View));
            Refresh();
        }

        PreviewDisplayViewState displayState = _controller.LoadView(viewItem.View);
        _isLoadingView = true;
        try
        {
            _currentViewData = displayState.SourceViewData;
            _currentDisplayState = displayState;
            _canvas.ConfigureBasemap(
                displayState.MapContext,
                _controller.BasemapSettings);
            _canvas.SurveyPointMarkerLabel = "0,0";
            _canvas.SetViewData(displayState.DisplayFeatures, displayState.DisplayBounds, displayState.DisplaySurveyPoint);
            UpdateBasemapAvailability();
            UpdateSurveyPointAvailability();
            ApplyCanvasFilters();
            PopulateLegend(displayState.SourceViewData);
            PopulateWarnings(displayState.SourceViewData);
            PopulateUnassignedFloors(displayState.SourceViewData);
            UpdateDetails(null);
            UpdateAssignmentControls();
            RefreshStatusText();
        }
        finally
        {
            _isLoadingView = false;
            UseWaitCursor = false;
            Cursor.Current = Cursors.Default;
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
        _canvas.ShowBasemap = _basemapCheckBox.Enabled && _basemapCheckBox.Checked;
        _canvas.ShowSurveyPoint = _surveyPointCheckBox.Enabled && _surveyPointCheckBox.Checked;
        _canvas.SearchText = _searchTextBox.Text;
        _canvas.RefreshFilters();
        RefreshStatusText();
    }
    private void PopulateLegend(PreviewViewData viewData)
    {
        _legendPanel.SuspendLayout();
        _legendPanel.Controls.Clear();

        List<PreviewFeatureData> legendFeatures = viewData.Features
            .Where(feature => feature.FeatureType == ExportFeatureType.Unit)
            .ToList();

        if (legendFeatures.Count == 0)
        {
            _legendPanel.Controls.Add(new Label
            {
                AutoSize = true,
                Text = T("No unit categories in this view.", "No unit categories in this view."),
                Margin = new Padding(0, 0, 0, 2),
            });
            _legendPanel.ResumeLayout();
            return;
        }

        var grouped = legendFeatures
            .GroupBy(
                feature => (feature.Category ?? string.Empty).Trim(),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, PreviewFeatureData> group in grouped)
        {
            PreviewFeatureData first = group.First();
            _legendPanel.Controls.Add(
                BuildLegendItem(
                    GetLegendLabel(group.Key),
                    group.Count(),
                    ParseColor(first.FillColorHex, DrawingColor.LightGray)));
        }

        _legendPanel.ResumeLayout();
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
        string? selectedFloorType = _controller.GetAssignmentState().SelectedFloorTypeName;
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
        PreviewDetailsSnapshot snapshot = _controller.BuildDetailsSnapshot(feature);
        List<string> lines = snapshot.Entries
            .Select(entry => $"{entry.Label}: {entry.Value}")
            .ToList();
        if (!string.IsNullOrWhiteSpace(snapshot.HelperText))
        {
            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add(snapshot.HelperText);
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
        _controller.SelectFeature(feature);
        UpdateAssignmentControls();
    }

    private void OnUnassignedFloorSelectionChanged()
    {
        if (_suppressUnassignedSelectionChanged)
        {
            return;
        }

        if (_unassignedFloorsListBox.SelectedItem is not UnassignedFloorItem item)
        {
            _controller.SelectUnassignedFloor(null, null);
            UpdateAssignmentControls();
            return;
        }

        _controller.SelectUnassignedFloor(item.Group.FloorTypeName, item.Group.ParsedZoneCandidate);
        UpdateAssignmentControls();
    }

    private void AssignSelectedFloorCategory()
    {
        if (_assignmentCategoryComboBox.SelectedItem is not string category ||
            string.IsNullOrWhiteSpace(category))
        {
            return;
        }

        PreviewDisplayViewState? displayState = _controller.StageCategoryOverride(GetSelectedFloorTypeNames(), category);
        if (displayState == null)
        {
            return;
        }

        _currentViewData = displayState.SourceViewData;
        _currentDisplayState = displayState;
        LoadSelectedView();
    }

    private void ClearSelectedFloorCategoryOverride()
    {
        PreviewDisplayViewState? displayState = _controller.ClearCategoryOverride(GetSelectedFloorTypeNames());
        if (displayState == null)
        {
            return;
        }

        _currentViewData = displayState.SourceViewData;
        _currentDisplayState = displayState;
        LoadSelectedView();
    }

    private void SavePendingAssignments()
    {
        PreviewDisplayViewState? displayState = _controller.SavePendingAssignments();
        if (displayState == null)
        {
            return;
        }

        _currentViewData = displayState.SourceViewData;
        _currentDisplayState = displayState;
        LoadSelectedView();
    }

    private void DiscardPendingAssignments()
    {
        PreviewDisplayViewState? displayState = _controller.DiscardPendingAssignments();
        if (displayState == null)
        {
            return;
        }

        _currentViewData = displayState.SourceViewData;
        _currentDisplayState = displayState;
        LoadSelectedView();
    }

    private void UpdateAssignmentControls()
    {
        PreviewAssignmentState state = _controller.GetAssignmentState();
        _assignmentTargetValueLabel.Text = state.TargetFloorType;
        _assignmentCandidateValueLabel.Text = state.ParsedCandidate;
        _assignmentCurrentValueLabel.Text = state.CurrentResolution;
        _assignmentCategoryComboBox.Enabled = state.CanChooseCategory;
        SelectAssignmentCategory(state.SuggestedCategory);
        _assignButton.Enabled = state.CanAssign;
        _clearAssignmentButton.Enabled = state.CanClear;
        _saveAssignmentsButton.Enabled = state.CanSave;
        _discardAssignmentsButton.Enabled = state.CanDiscard;
        _assignmentHintLabel.Text = state.HintText;
        SelectUnassignedFloorItem(state.SelectedFloorTypeName);
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
        return _controller.GetSelectedFloorTypeNames(_unassignedFloorsListBox.SelectedItems
            .OfType<UnassignedFloorItem>()
            .Select(item => item.Group.FloorTypeName));
    }

    private void SetStatusMessage(string message)
    {
        _statusLabel.Text = message ?? string.Empty;
    }

    private void RefreshStatusText()
    {
        _statusLabel.Text = _controller.BuildFooterStatus(
            _canvas.BasemapAvailable,
            _canvas.BasemapUnavailableReason,
            _canvas.BasemapAttribution,
            _canvas.ShowBasemap,
            _canvas.SurveyPointAvailable,
            _canvas.ShowSurveyPoint);
    }

    private void UpdateBasemapAvailability()
    {
        bool available = _controller.IsBasemapToggleAvailable(_canvas.BasemapAvailable);
        _basemapCheckBox.Enabled = available;
        if (!available)
        {
            _basemapCheckBox.Checked = false;
        }

        _canvas.ShowBasemap = available && _basemapCheckBox.Checked;
        RefreshStatusText();
    }

    private void UpdateSurveyPointAvailability()
    {
        bool available = _controller.IsSurveyPointToggleAvailable(_canvas.SurveyPointAvailable);
        _surveyPointCheckBox.Enabled = available;
        if (!available)
        {
            _surveyPointCheckBox.Checked = false;
        }

        _canvas.ShowSurveyPoint = available && _surveyPointCheckBox.Checked;
        RefreshStatusText();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _controller.DiscardPendingChangesOnClose();
    }

    private string L(string key, string fallback) => LocalizedTextProvider.Get(_language, key, fallback);

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

    private static WinFormsControl BuildLegendItem(string label, int count, DrawingColor color)
    {
        FlowLayoutPanel item = new()
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 12, 6),
            Padding = Padding.Empty,
        };

        System.Windows.Forms.Panel swatch = new()
        {
            Size = new Size(14, 14),
            BackColor = color,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 3, 6, 0),
        };
        item.Controls.Add(swatch);
        item.Controls.Add(new Label
        {
            AutoSize = true,
            Text = $"{label} ({count})",
            Margin = Padding.Empty,
        });

        return item;
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



