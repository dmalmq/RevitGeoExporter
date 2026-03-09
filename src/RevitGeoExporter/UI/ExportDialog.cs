using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using RevitGeoExporter.Help;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Export;
using WinFormsControl = System.Windows.Forms.Control;
using WinFormsForm = System.Windows.Forms.Form;

namespace RevitGeoExporter.UI;

public sealed class ExportDialog : WinFormsForm
{
    private readonly ComboBox _languageComboBox = new();
    private readonly CheckedListBox _viewList = new();
    private readonly TextBox _outputDirectoryTextBox = new();
    private readonly ComboBox _crsPresetComboBox = new();
    private readonly TextBox _targetEpsgTextBox = new();
    private readonly ComboBox _profileComboBox = new();
    private readonly CheckBox _unitCheckBox = new();
    private readonly CheckBox _detailCheckBox = new();
    private readonly CheckBox _openingCheckBox = new();
    private readonly CheckBox _levelCheckBox = new();
    private readonly CheckBox _diagnosticsCheckBox = new();
    private readonly CheckBox _packageCheckBox = new();
    private readonly CheckBox _packageLegendCheckBox = new();
    private readonly CheckBox _repairEnabledCheckBox = new();
    private readonly TextBox _minPolygonAreaTextBox = new();
    private readonly TextBox _minOpeningLengthTextBox = new();
    private readonly TextBox _simplifyToleranceTextBox = new();
    private readonly TextBox _openingSnapDistanceTextBox = new();
    private readonly TextBox _elevatorSnapDistanceTextBox = new();
    private readonly TextBox _mergeBoundaryThresholdTextBox = new();
    private readonly GroupBox _viewsGroup = new();
    private readonly GroupBox _optionsGroup = new();
    private readonly Button _selectAllButton = new();
    private readonly Button _clearAllButton = new();
    private readonly Button _browseButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _previewButton = new();
    private readonly Button _exportButton = new();
    private readonly Button _helpButton = new();
    private readonly Label _versionLabel = new();
    private readonly Label _profilesLabel = new();
    private readonly Label _languageLabel = new();
    private readonly Label _featureTypesLabel = new();
    private readonly Label _outputDirectoryLabel = new();
    private readonly Label _crsLabel = new();

    private readonly IReadOnlyList<ViewSelectionItem> _viewItems;
    private readonly Action<ExportPreviewRequest>? _previewRequested;
    private readonly Action<ExportProfileScope, string, ExportDialogSettings>? _saveProfileRequested;
    private readonly Action<ExportProfile, string>? _renameProfileRequested;
    private readonly Action<ExportProfile>? _deleteProfileRequested;
    private readonly Action? _openMappingsRequested;
    private readonly List<ExportProfile> _profiles;
    private UiLanguage _language = UiLanguage.English;
    private bool _isApplyingProfile;

    public ExportDialog(
        IReadOnlyList<ViewPlan> views,
        ExportDialogSettings settings,
        IReadOnlyList<ExportProfile>? profiles = null,
        Action<ExportProfileScope, string, ExportDialogSettings>? saveProfileRequested = null,
        Action<ExportProfile, string>? renameProfileRequested = null,
        Action<ExportProfile>? deleteProfileRequested = null,
        Action? openMappingsRequested = null,
        Action<ExportPreviewRequest>? previewRequested = null)
    {
        if (views is null)
        {
            throw new ArgumentNullException(nameof(views));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        _viewItems = views
            .Select(view => new ViewSelectionItem(view))
            .ToList();
        _profiles = (profiles ?? Array.Empty<ExportProfile>()).ToList();
        _saveProfileRequested = saveProfileRequested;
        _renameProfileRequested = renameProfileRequested;
        _deleteProfileRequested = deleteProfileRequested;
        _openMappingsRequested = openMappingsRequested;
        _previewRequested = previewRequested;

        InitializeComponents();
        LoadValues(settings);
    }

    public ExportDialogResult? Result { get; private set; }

    private void InitializeComponents()
    {
        Text = "Export GeoPackage";
        Width = 900;
        Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.Sizable;

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        Controls.Add(root);

        TableLayoutPanel content = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10, 10, 10, 0),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
        root.Controls.Add(content, 0, 0);

        content.Controls.Add(BuildViewsPanel(), 0, 0);
        content.Controls.Add(BuildOptionsPanel(), 1, 0);

        root.Controls.Add(BuildActionsPanel(), 0, 1);
    }

    private WinFormsControl BuildViewsPanel()
    {
        _viewsGroup.Dock = DockStyle.Fill;

        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        _viewsGroup.Controls.Add(panel);

        _viewList.Dock = DockStyle.Fill;
        _viewList.CheckOnClick = true;
        _viewList.HorizontalScrollbar = true;
        _viewList.IntegralHeight = false;
        _viewList.ItemCheck += OnViewListItemCheck;
        panel.Controls.Add(_viewList, 0, 0);

        FlowLayoutPanel viewActions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };

        _selectAllButton.Width = 100;
        _selectAllButton.Height = 28;
        _selectAllButton.Click += (_, _) => CheckAllViews();

        _clearAllButton.Width = 100;
        _clearAllButton.Height = 28;
        _clearAllButton.Click += (_, _) =>
        {
            for (int i = 0; i < _viewList.Items.Count; i++)
            {
                _viewList.SetItemChecked(i, false);
            }
        };

        viewActions.Controls.Add(_selectAllButton);
        viewActions.Controls.Add(_clearAllButton);
        panel.Controls.Add(viewActions, 0, 1);

        return _viewsGroup;
    }

    private WinFormsControl BuildOptionsPanel()
    {
        _optionsGroup.Dock = DockStyle.Fill;

        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 15,
            AutoScroll = true,
            Padding = new Padding(10),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 120f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 178f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _optionsGroup.Controls.Add(panel);

        _profilesLabel.Dock = DockStyle.Fill;
        _profilesLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_profilesLabel, 0, 0);

        panel.Controls.Add(BuildProfilesPanel(), 0, 1);

        _languageLabel.Dock = DockStyle.Fill;
        _languageLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_languageLabel, 0, 2);

        _languageComboBox.Dock = DockStyle.Fill;
        _languageComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageComboBox.Items.Add(new LanguageItem(UiLanguage.English));
        _languageComboBox.Items.Add(new LanguageItem(UiLanguage.Japanese));
        _languageComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (_languageComboBox.SelectedItem is LanguageItem selected)
            {
                _language = selected.Language;
                ViewSelectionItem.DisplayLanguage = _language;
                _viewList.Refresh();
                ApplyLanguage();
                UpdateProfileText();
                UpdateDiagnosticsText();
                UpdateVersionLabel();
            }
        };
        panel.Controls.Add(_languageComboBox, 0, 3);

        _featureTypesLabel.Dock = DockStyle.Fill;
        _featureTypesLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_featureTypesLabel, 0, 4);

        FlowLayoutPanel featuresPanel = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };

        _unitCheckBox.Text = "unit";
        _detailCheckBox.Text = "detail";
        _openingCheckBox.Text = "opening";
        _levelCheckBox.Text = "level";
        _diagnosticsCheckBox.AutoSize = true;
        _packageCheckBox.AutoSize = true;
        _packageLegendCheckBox.AutoSize = true;
        _unitCheckBox.CheckedChanged += (_, _) => UpdatePreviewButtonEnabled();
        _detailCheckBox.CheckedChanged += (_, _) => UpdatePreviewButtonEnabled();
        _openingCheckBox.CheckedChanged += (_, _) => UpdatePreviewButtonEnabled();
        _levelCheckBox.CheckedChanged += (_, _) => UpdatePreviewButtonEnabled();
        _packageCheckBox.CheckedChanged += (_, _) => _packageLegendCheckBox.Enabled = _packageCheckBox.Checked;
        featuresPanel.Controls.Add(_unitCheckBox);
        featuresPanel.Controls.Add(_detailCheckBox);
        featuresPanel.Controls.Add(_openingCheckBox);
        featuresPanel.Controls.Add(_levelCheckBox);
        featuresPanel.Controls.Add(_diagnosticsCheckBox);
        featuresPanel.Controls.Add(_packageCheckBox);
        featuresPanel.Controls.Add(_packageLegendCheckBox);
        panel.Controls.Add(featuresPanel, 0, 5);

        _outputDirectoryLabel.Dock = DockStyle.Fill;
        _outputDirectoryLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_outputDirectoryLabel, 0, 6);

        TableLayoutPanel outputPanel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
        };
        outputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));

        _outputDirectoryTextBox.Dock = DockStyle.Fill;
        outputPanel.Controls.Add(_outputDirectoryTextBox, 0, 0);

        _browseButton.Width = 84;
        _browseButton.Height = 28;
        _browseButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _browseButton.Click += (_, _) =>
        {
            using FolderBrowserDialog folderDialog = new()
            {
                Description = "Select output folder for GeoPackage files",
                ShowNewFolderButton = true,
                SelectedPath = _outputDirectoryTextBox.Text,
            };

            if (folderDialog.ShowDialog(this) == DialogResult.OK)
            {
                _outputDirectoryTextBox.Text = folderDialog.SelectedPath;
            }
        };
        outputPanel.Controls.Add(_browseButton, 1, 0);
        panel.Controls.Add(outputPanel, 0, 7);

        _crsLabel.Dock = DockStyle.Fill;
        _crsLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_crsLabel, 0, 8);

        TableLayoutPanel crsPanel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        crsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        crsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));

        _crsPresetComboBox.Dock = DockStyle.Fill;
        _crsPresetComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (KeyValuePair<int, string> zone in JapanPlaneRectangular.Zones.OrderBy(x => x.Key))
        {
            _crsPresetComboBox.Items.Add(new CrsPresetItem(zone.Key, zone.Value));
        }

        _crsPresetComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (_crsPresetComboBox.SelectedItem is CrsPresetItem selected)
            {
                _targetEpsgTextBox.Text = selected.Epsg.ToString();
            }
        };
        crsPanel.Controls.Add(_crsPresetComboBox, 0, 0);

        _targetEpsgTextBox.Dock = DockStyle.Fill;
        crsPanel.Controls.Add(_targetEpsgTextBox, 0, 1);
        panel.Controls.Add(crsPanel, 0, 9);

        Button mappingsButton = new()
        {
            Dock = DockStyle.Left,
            Width = 120,
        };
        mappingsButton.Click += (_, _) => _openMappingsRequested?.Invoke();
        mappingsButton.Text = "Mappings...";
        panel.Controls.Add(mappingsButton, 0, 10);

        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Geometry Repair",
        }, 0, 11);
        panel.Controls.Add(BuildRepairPanel(), 0, 12);

        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Packaging",
        }, 0, 13);
        panel.Controls.Add(BuildPackagingPanel(), 0, 14);

        return _optionsGroup;
    }

    private WinFormsControl BuildPackagingPanel()
    {
        Label label = new()
        {
            Dock = DockStyle.Fill,
            Text = "When enabled, export also writes a package folder with preview images, a manifest, and optional legend.",
            AutoEllipsis = false,
        };
        return label;
    }

    private WinFormsControl BuildRepairPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180f));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        AddRepairRow(panel, 0, "Enable repair", _repairEnabledCheckBox);
        AddRepairRow(panel, 1, "Min polygon area (m2)", _minPolygonAreaTextBox);
        AddRepairRow(panel, 2, "Min opening length (m)", _minOpeningLengthTextBox);
        AddRepairRow(panel, 3, "Simplify tolerance (m)", _simplifyToleranceTextBox);
        AddRepairRow(panel, 4, "Opening snap distance (m)", _openingSnapDistanceTextBox);
        AddRepairRow(panel, 5, "Elevator snap distance (m)", _elevatorSnapDistanceTextBox);
        AddRepairRow(panel, 6, "Merge gap threshold (m)", _mergeBoundaryThresholdTextBox);
        return panel;
    }

    private static void AddRepairRow(TableLayoutPanel panel, int rowIndex, string labelText, WinFormsControl control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        Label label = new()
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = labelText,
        };
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(label, 0, rowIndex);
        panel.Controls.Add(control, 1, rowIndex);
    }

    private WinFormsControl BuildProfilesPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));

        _profileComboBox.Dock = DockStyle.Fill;
        _profileComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _profileComboBox.SelectedIndexChanged += (_, _) => ApplySelectedProfile();
        panel.Controls.Add(_profileComboBox, 0, 0);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
        };

        Button saveProjectButton = new() { Width = 88, Height = 26, Text = "Save Project" };
        saveProjectButton.Click += (_, _) => SaveProfile(ExportProfileScope.Project);
        actions.Controls.Add(saveProjectButton);

        Button saveGlobalButton = new() { Width = 88, Height = 26, Text = "Save Global" };
        saveGlobalButton.Click += (_, _) => SaveProfile(ExportProfileScope.Global);
        actions.Controls.Add(saveGlobalButton);

        Button renameButton = new() { Width = 72, Height = 26, Text = "Rename" };
        renameButton.Click += (_, _) => RenameSelectedProfile();
        actions.Controls.Add(renameButton);

        Button deleteButton = new() { Width = 72, Height = 26, Text = "Delete" };
        deleteButton.Click += (_, _) => DeleteSelectedProfile();
        actions.Controls.Add(deleteButton);

        panel.Controls.Add(actions, 0, 1);
        return panel;
    }

    private WinFormsControl BuildActionsPanel()
    {
        TableLayoutPanel container = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(10, 8, 10, 8),
        };
        container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _versionLabel.Dock = DockStyle.Fill;
        _versionLabel.TextAlign = ContentAlignment.MiddleLeft;
        container.Controls.Add(_versionLabel, 0, 0);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
        };

        _cancelButton.Width = 90;
        _cancelButton.Height = 30;
        _cancelButton.DialogResult = DialogResult.Cancel;

        _exportButton.Width = 90;
        _exportButton.Height = 30;
        _exportButton.Click += (_, _) => ConfirmExport();

        _previewButton.Width = 90;
        _previewButton.Height = 30;
        _previewButton.Click += (_, _) => ShowPreview();

        _helpButton.Width = 90;
        _helpButton.Height = 30;
        _helpButton.Text = UiLanguageText.Select(_language, "Help", "ヘルプ");
        _helpButton.Click += (_, _) => HelpLauncher.Show(this, HelpTopic.ExportWorkflow, _language, Text);

        actions.Controls.Add(_cancelButton);
        actions.Controls.Add(_exportButton);
        actions.Controls.Add(_previewButton);
        actions.Controls.Add(_helpButton);
        AcceptButton = _exportButton;
        CancelButton = _cancelButton;

        container.Controls.Add(actions, 1, 0);
        return container;
    }

    private void LoadValues(ExportDialogSettings settings)
    {
        _language = Enum.IsDefined(typeof(UiLanguage), settings.UiLanguage)
            ? settings.UiLanguage
            : UiLanguage.English;
        ViewSelectionItem.DisplayLanguage = _language;

        foreach (ViewSelectionItem item in _viewItems)
        {
            _viewList.Items.Add(item, false);
        }

        HashSet<long> selectedIds = new(settings.SelectedViewIds ?? new List<long>());
        if (selectedIds.Count > 0)
        {
            bool anyMatched = false;
            for (int i = 0; i < _viewItems.Count; i++)
            {
                bool isSelected = selectedIds.Contains(_viewItems[i].View.Id.Value);
                _viewList.SetItemChecked(i, isSelected);
                anyMatched |= isSelected;
            }

            if (!anyMatched)
            {
                CheckAllViews();
            }
        }
        else
        {
            CheckAllViews();
        }

        _outputDirectoryTextBox.Text = settings.OutputDirectory ?? string.Empty;
        PopulateProfiles();

        ExportFeatureType featureTypes =
            settings.FeatureTypes == ExportFeatureType.None ? ExportFeatureType.All : settings.FeatureTypes;
        _unitCheckBox.Checked = featureTypes.HasFlag(ExportFeatureType.Unit);
        _detailCheckBox.Checked = featureTypes.HasFlag(ExportFeatureType.Detail);
        _openingCheckBox.Checked = featureTypes.HasFlag(ExportFeatureType.Opening);
        _levelCheckBox.Checked = featureTypes.HasFlag(ExportFeatureType.Level);
        _diagnosticsCheckBox.Checked = settings.GenerateDiagnosticsReport;
        _packageCheckBox.Checked = settings.GeneratePackageOutput;
        _packageLegendCheckBox.Checked = settings.IncludePackageLegend;
        _packageLegendCheckBox.Enabled = _packageCheckBox.Checked;
        LoadGeometryRepairOptions(settings.GeometryRepairOptions);

        _targetEpsgTextBox.Text = settings.TargetEpsg > 0
            ? settings.TargetEpsg.ToString()
            : ProjectInfo.DefaultTargetEpsg.ToString();

        SelectPresetIfAvailable(settings.TargetEpsg);
        SelectLanguage(_language);
        ApplyLanguage();
        UpdateProfileText();
        UpdateDiagnosticsText();
        UpdateVersionLabel();
        UpdatePreviewButtonEnabled();
    }

    private void ApplyLanguage()
    {
        Text = UiLanguageText.Select(_language, "Export GeoPackage", "GeoPackageをエクスポート");
        _viewsGroup.Text = UiLanguageText.Select(_language, "Plan Views", "平面図ビュー");
        _optionsGroup.Text = UiLanguageText.Select(_language, "Export Options", "エクスポート設定");
        _languageLabel.Text = UiLanguageText.Select(_language, "Language", "言語");
        _featureTypesLabel.Text = UiLanguageText.Select(_language, "Feature Types", "フィーチャ種別");
        _outputDirectoryLabel.Text = UiLanguageText.Select(_language, "Output Directory", "出力フォルダ");
        _crsLabel.Text = UiLanguageText.Select(_language, "CRS (EPSG)", "CRS (EPSG)");
        _selectAllButton.Text = UiLanguageText.Select(_language, "Select All", "全て選択");
        _clearAllButton.Text = UiLanguageText.Select(_language, "Clear All", "全て解除");
        _browseButton.Text = UiLanguageText.Select(_language, "Browse...", "参照...");
        _cancelButton.Text = UiLanguageText.Select(_language, "Cancel", "キャンセル");
        _previewButton.Text = UiLanguageText.Select(_language, "Preview...", "プレビュー...");
        _exportButton.Text = UiLanguageText.Select(_language, "Export", "エクスポート");
        _packageCheckBox.Text = UiLanguageText.Select(_language, "Write GIS package", "GISパッケージを出力");
        _packageLegendCheckBox.Text = UiLanguageText.Select(_language, "Include legend file", "凡例ファイルを含める");
    }

    private void SelectLanguage(UiLanguage language)
    {
        for (int i = 0; i < _languageComboBox.Items.Count; i++)
        {
            if (_languageComboBox.Items[i] is LanguageItem item && item.Language == language)
            {
                _languageComboBox.SelectedIndex = i;
                return;
            }
        }

        _languageComboBox.SelectedIndex = 0;
    }

    private void UpdateVersionLabel()
    {
        _versionLabel.Text = _language == UiLanguage.Japanese
            ? $"Version {ProjectInfo.VersionTag}"
            : $"Version {ProjectInfo.VersionTag}";
    }

    private void UpdateDiagnosticsText()
    {
        _diagnosticsCheckBox.Text = UiLanguageText.Select(
            _language,
            "Write diagnostics report",
            "診断レポートを出力");
    }

    private void UpdateProfileText()
    {
        _profilesLabel.Text = UiLanguageText.Select(_language, "Export Profiles", "エクスポートプロファイル");
        _profileComboBox.Refresh();
    }

    private void ConfirmExport()
    {
        List<ViewPlan> selectedViews = GetSelectedViews();
        if (selectedViews.Count == 0)
        {
            MessageBox.Show(
                this,
                UiLanguageText.Select(_language, "Select at least one plan view to export.", "エクスポートする平面図ビューを1つ以上選択してください。"),
                ProjectInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ExportFeatureType featureTypes = GetSelectedFeatureTypes();
        if (featureTypes == ExportFeatureType.None)
        {
            MessageBox.Show(
                this,
                UiLanguageText.Select(_language, "Select at least one feature type.", "フィーチャ種別を1つ以上選択してください。"),
                ProjectInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        string outputDirectory = (_outputDirectoryTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            MessageBox.Show(
                this,
                UiLanguageText.Select(_language, "Choose an output directory.", "出力フォルダを選択してください。"),
                ProjectInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!int.TryParse(_targetEpsgTextBox.Text, out int epsg) || epsg <= 0)
        {
            MessageBox.Show(
                this,
                UiLanguageText.Select(_language, "Enter a valid EPSG code.", "有効なEPSGコードを入力してください。"),
                ProjectInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Result = new ExportDialogResult(
            selectedViews,
            outputDirectory,
            epsg,
            featureTypes,
            _diagnosticsCheckBox.Checked,
            _packageCheckBox.Checked,
            _packageLegendCheckBox.Checked,
            BuildGeometryRepairOptions(),
            (_profileComboBox.SelectedItem as ProfileItem)?.Profile?.Name,
            _language);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ShowPreview()
    {
        if (_previewRequested == null)
        {
            return;
        }

        List<ViewPlan> selectedViews = GetSelectedViews();
        if (selectedViews.Count == 0)
        {
            MessageBox.Show(
                this,
                UiLanguageText.Select(_language, "Select at least one plan view to preview.", "プレビューする平面図ビューを1つ以上選択してください。"),
                ProjectInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ExportFeatureType previewTypes = GetSelectedFeatureTypes();
        if (previewTypes == ExportFeatureType.None)
        {
            MessageBox.Show(
                this,
                UiLanguageText.Select(_language, "Preview requires at least one selected feature type.", "プレビューには少なくとも1つの選択済みフィーチャー種別が必要です。"),
                ProjectInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _previewRequested(new ExportPreviewRequest(selectedViews, previewTypes, BuildGeometryRepairOptions(), _language));
    }

    private void CheckAllViews()
    {
        for (int i = 0; i < _viewList.Items.Count; i++)
        {
            _viewList.SetItemChecked(i, true);
        }
    }

    private List<ViewPlan> GetSelectedViews()
    {
        List<ViewPlan> selected = new();
        foreach (object item in _viewList.CheckedItems)
        {
            if (item is ViewSelectionItem viewItem)
            {
                selected.Add(viewItem.View);
            }
        }

        return selected;
    }

    private ExportFeatureType GetSelectedFeatureTypes()
    {
        ExportFeatureType types = ExportFeatureType.None;
        if (_unitCheckBox.Checked)
        {
            types |= ExportFeatureType.Unit;
        }

        if (_detailCheckBox.Checked)
        {
            types |= ExportFeatureType.Detail;
        }

        if (_openingCheckBox.Checked)
        {
            types |= ExportFeatureType.Opening;
        }

        if (_levelCheckBox.Checked)
        {
            types |= ExportFeatureType.Level;
        }

        return types;
    }

    private void UpdatePreviewButtonEnabled()
    {
        ExportFeatureType previewTypes = GetSelectedFeatureTypes();
        _previewButton.Enabled = _previewRequested != null &&
                                 previewTypes != ExportFeatureType.None &&
                                 GetSelectedViews().Count > 0;
    }

    private void OnViewListItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_viewList.IsHandleCreated)
        {
            _viewList.BeginInvoke(new Action(UpdatePreviewButtonEnabled));
            return;
        }

        if (IsHandleCreated)
        {
            BeginInvoke(new Action(UpdatePreviewButtonEnabled));
            return;
        }

        // During initial dialog population the checked state changes before any handle exists.
        // In that phase a direct refresh is safe, and OnShown performs a final sync once the form is visible.
        UpdatePreviewButtonEnabled();
    }

    private void SelectPresetIfAvailable(int targetEpsg)
    {
        for (int i = 0; i < _crsPresetComboBox.Items.Count; i++)
        {
            if (_crsPresetComboBox.Items[i] is CrsPresetItem item && item.Epsg == targetEpsg)
            {
                _crsPresetComboBox.SelectedIndex = i;
                return;
            }
        }

        _crsPresetComboBox.SelectedIndex = -1;
    }

    public ExportDialogSettings BuildSettings()
    {
        int targetEpsg = int.TryParse(_targetEpsgTextBox.Text, out int epsg)
            ? epsg
            : ProjectInfo.DefaultTargetEpsg;

        return new ExportDialogSettings
        {
            OutputDirectory = (_outputDirectoryTextBox.Text ?? string.Empty).Trim(),
            TargetEpsg = targetEpsg,
            FeatureTypes = GetSelectedFeatureTypes(),
            SelectedViewIds = GetSelectedViews().Select(x => x.Id.Value).ToList(),
            GenerateDiagnosticsReport = _diagnosticsCheckBox.Checked,
            GeneratePackageOutput = _packageCheckBox.Checked,
            IncludePackageLegend = _packageLegendCheckBox.Checked,
            GeometryRepairOptions = BuildGeometryRepairOptions(),
            UiLanguage = _language,
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (string.IsNullOrWhiteSpace(_outputDirectoryTextBox.Text))
        {
            _outputDirectoryTextBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        UpdatePreviewButtonEnabled();
    }

    private void PopulateProfiles()
    {
        _profileComboBox.Items.Clear();
        _profileComboBox.Items.Add(ProfileItem.CreateCurrent(_language));
        ProfileItem.DisplayLanguage = _language;
        foreach (ExportProfile profile in _profiles
                     .OrderBy(profile => profile.Scope)
                     .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase))
        {
            _profileComboBox.Items.Add(new ProfileItem(profile));
        }

        _profileComboBox.SelectedIndex = 0;
    }

    private void ApplySelectedProfile()
    {
        if (_isApplyingProfile)
        {
            return;
        }

        if (_profileComboBox.SelectedItem is not ProfileItem item || item.Profile == null)
        {
            return;
        }

        _isApplyingProfile = true;
        try
        {
            ExportDialogSettings settings = item.Profile.ToSettings();
            _outputDirectoryTextBox.Text = settings.OutputDirectory;
            _targetEpsgTextBox.Text = settings.TargetEpsg.ToString();
            _unitCheckBox.Checked = settings.FeatureTypes.HasFlag(ExportFeatureType.Unit);
            _detailCheckBox.Checked = settings.FeatureTypes.HasFlag(ExportFeatureType.Detail);
            _openingCheckBox.Checked = settings.FeatureTypes.HasFlag(ExportFeatureType.Opening);
            _levelCheckBox.Checked = settings.FeatureTypes.HasFlag(ExportFeatureType.Level);
            _diagnosticsCheckBox.Checked = settings.GenerateDiagnosticsReport;
            _packageCheckBox.Checked = settings.GeneratePackageOutput;
            _packageLegendCheckBox.Checked = settings.IncludePackageLegend;
            _packageLegendCheckBox.Enabled = _packageCheckBox.Checked;
            LoadGeometryRepairOptions(settings.GeometryRepairOptions);
            SelectPresetIfAvailable(settings.TargetEpsg);
            SelectLanguage(settings.UiLanguage);
        }
        finally
        {
            _isApplyingProfile = false;
        }
    }

    private void SaveProfile(ExportProfileScope scope)
    {
        if (_saveProfileRequested == null)
        {
            return;
        }

        using TextPromptForm prompt = new(
            "Save Export Profile",
            "Profile name",
            (_profileComboBox.SelectedItem as ProfileItem)?.Profile?.Scope == scope
                ? (_profileComboBox.SelectedItem as ProfileItem)?.Profile?.Name ?? string.Empty
                : string.Empty);
        if (prompt.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        string name = prompt.Value;
        if (name.Length == 0)
        {
            return;
        }

        ExportDialogSettings settings = BuildSettings();
        _saveProfileRequested(scope, name, settings);

        ExportProfile profile = ExportProfile.FromSettings(name, scope, settings);
        int existingIndex = _profiles.FindIndex(candidate =>
            candidate.Scope == scope &&
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            _profiles[existingIndex] = profile;
        }
        else
        {
            _profiles.Add(profile);
        }

        PopulateProfiles();
        SelectProfile(profile);
    }

    private void DeleteSelectedProfile()
    {
        if (_deleteProfileRequested == null ||
            _profileComboBox.SelectedItem is not ProfileItem item ||
            item.Profile == null)
        {
            return;
        }

        _deleteProfileRequested(item.Profile);
        _profiles.RemoveAll(candidate =>
            candidate.Scope == item.Profile.Scope &&
            string.Equals(candidate.Name, item.Profile.Name, StringComparison.OrdinalIgnoreCase));
        PopulateProfiles();
    }

    private void RenameSelectedProfile()
    {
        if (_renameProfileRequested == null ||
            _profileComboBox.SelectedItem is not ProfileItem item ||
            item.Profile == null)
        {
            return;
        }

        using TextPromptForm prompt = new(
            "Rename Export Profile",
            "New profile name",
            item.Profile.Name);
        if (prompt.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        string newName = prompt.Value;
        if (newName.Length == 0 ||
            string.Equals(newName, item.Profile.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _renameProfileRequested(item.Profile, newName);
        item.Profile.Name = newName;
        PopulateProfiles();
        SelectProfile(item.Profile);
    }

    private void SelectProfile(ExportProfile profile)
    {
        for (int i = 0; i < _profileComboBox.Items.Count; i++)
        {
            if (_profileComboBox.Items[i] is ProfileItem item &&
                item.Profile != null &&
                item.Profile.Scope == profile.Scope &&
                string.Equals(item.Profile.Name, profile.Name, StringComparison.OrdinalIgnoreCase))
            {
                _profileComboBox.SelectedIndex = i;
                return;
            }
        }

        _profileComboBox.SelectedIndex = 0;
    }

    private void LoadGeometryRepairOptions(GeometryRepairOptions? options)
    {
        GeometryRepairOptions value = (options ?? new GeometryRepairOptions()).Clone();
        _repairEnabledCheckBox.Checked = value.Enabled;
        _minPolygonAreaTextBox.Text = value.MinimumPolygonAreaSquareMeters.ToString("0.###");
        _minOpeningLengthTextBox.Text = value.MinimumOpeningLengthMeters.ToString("0.###");
        _simplifyToleranceTextBox.Text = value.SimplifyToleranceMeters.ToString("0.###");
        _openingSnapDistanceTextBox.Text = value.OpeningSnapDistanceMeters.ToString("0.###");
        _elevatorSnapDistanceTextBox.Text = value.ElevatorOpeningSnapDistanceMeters.ToString("0.###");
        _mergeBoundaryThresholdTextBox.Text = value.MergeNearbyBoundaryThresholdMeters.ToString("0.###");
    }

    private GeometryRepairOptions BuildGeometryRepairOptions()
    {
        return new GeometryRepairOptions
        {
            Enabled = _repairEnabledCheckBox.Checked,
            MinimumPolygonAreaSquareMeters = ParseDouble(_minPolygonAreaTextBox.Text, 0.01d),
            MinimumOpeningLengthMeters = ParseDouble(_minOpeningLengthTextBox.Text, 0.10d),
            SimplifyToleranceMeters = ParseDouble(_simplifyToleranceTextBox.Text, 0d),
            OpeningSnapDistanceMeters = ParseDouble(_openingSnapDistanceTextBox.Text, 5.0d),
            ElevatorOpeningSnapDistanceMeters = ParseDouble(_elevatorSnapDistanceTextBox.Text, 5.0d),
            MergeNearbyBoundaryThresholdMeters = ParseDouble(_mergeBoundaryThresholdTextBox.Text, 0.15d),
        };
    }

    private static double ParseDouble(string? value, double fallback)
    {
        return double.TryParse(value, out double parsed) ? parsed : fallback;
    }

    private sealed class ViewSelectionItem
    {
        public static UiLanguage DisplayLanguage { get; set; } = UiLanguage.English;

        public ViewSelectionItem(ViewPlan view)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
        }

        public ViewPlan View { get; }

        public override string ToString()
        {
            string levelName = View.GenLevel?.Name ?? UiLanguageText.Select(DisplayLanguage, "<no level>", "<レベルなし>");
            string levelLabel = UiLanguageText.Select(DisplayLanguage, "Level", "レベル");
            return $"{View.Name}  [{levelLabel}: {levelName}]";
        }
    }

    private sealed class LanguageItem
    {
        public LanguageItem(UiLanguage language)
        {
            Language = language;
        }

        public UiLanguage Language { get; }

        public override string ToString()
        {
            return UiLanguageText.DisplayName(Language);
        }
    }

    private sealed class CrsPresetItem
    {
        public CrsPresetItem(int epsg, string zoneName)
        {
            Epsg = epsg;
            ZoneName = zoneName;
        }

        public int Epsg { get; }

        public string ZoneName { get; }

        public override string ToString()
        {
            return $"EPSG:{Epsg} - {ZoneName}";
        }
    }

    private sealed class ProfileItem
    {
        public static UiLanguage DisplayLanguage { get; set; } = UiLanguage.English;

        public ProfileItem(ExportProfile? profile)
        {
            Profile = profile;
        }

        public ExportProfile? Profile { get; }

        public static ProfileItem CreateCurrent(UiLanguage language)
        {
            DisplayLanguage = language;
            return new ProfileItem(null);
        }

        public override string ToString()
        {
            if (Profile == null)
            {
                return DisplayLanguage == UiLanguage.Japanese ? "(現在の設定)" : "(Current settings)";
            }

            string scopeLabel = Profile.Scope == ExportProfileScope.Project ? "Project" : "Global";
            return $"[{scopeLabel}] {Profile.Name}";
        }
    }
}
