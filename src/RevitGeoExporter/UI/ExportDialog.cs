using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Coordinates;
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
    private readonly CheckBox _unitCheckBox = new();
    private readonly CheckBox _detailCheckBox = new();
    private readonly CheckBox _openingCheckBox = new();
    private readonly CheckBox _levelCheckBox = new();
    private readonly GroupBox _viewsGroup = new();
    private readonly GroupBox _optionsGroup = new();
    private readonly Button _selectAllButton = new();
    private readonly Button _clearAllButton = new();
    private readonly Button _browseButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _previewButton = new();
    private readonly Button _exportButton = new();
    private readonly Label _versionLabel = new();
    private readonly Label _languageLabel = new();
    private readonly Label _featureTypesLabel = new();
    private readonly Label _outputDirectoryLabel = new();
    private readonly Label _crsLabel = new();

    private readonly IReadOnlyList<ViewSelectionItem> _viewItems;
    private readonly Action<ExportPreviewRequest>? _previewRequested;
    private UiLanguage _language = UiLanguage.English;

    public ExportDialog(
        IReadOnlyList<ViewPlan> views,
        ExportDialogSettings settings,
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
        _viewList.ItemCheck += (_, _) => BeginInvoke(new Action(UpdatePreviewButtonEnabled));
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
            RowCount = 10,
            Padding = new Padding(10),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 120f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _optionsGroup.Controls.Add(panel);

        _languageLabel.Dock = DockStyle.Fill;
        _languageLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_languageLabel, 0, 0);

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
                UpdateVersionLabel();
            }
        };
        panel.Controls.Add(_languageComboBox, 0, 1);

        _featureTypesLabel.Dock = DockStyle.Fill;
        _featureTypesLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_featureTypesLabel, 0, 2);

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
        _unitCheckBox.CheckedChanged += (_, _) => UpdatePreviewButtonEnabled();
        _detailCheckBox.CheckedChanged += (_, _) => UpdatePreviewButtonEnabled();
        _openingCheckBox.CheckedChanged += (_, _) => UpdatePreviewButtonEnabled();
        _levelCheckBox.CheckedChanged += (_, _) => UpdatePreviewButtonEnabled();
        featuresPanel.Controls.Add(_unitCheckBox);
        featuresPanel.Controls.Add(_detailCheckBox);
        featuresPanel.Controls.Add(_openingCheckBox);
        featuresPanel.Controls.Add(_levelCheckBox);
        panel.Controls.Add(featuresPanel, 0, 3);

        _outputDirectoryLabel.Dock = DockStyle.Fill;
        _outputDirectoryLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_outputDirectoryLabel, 0, 5);

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
        panel.Controls.Add(outputPanel, 0, 6);

        _crsLabel.Dock = DockStyle.Fill;
        _crsLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_crsLabel, 0, 7);

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
        panel.Controls.Add(crsPanel, 0, 8);

        return _optionsGroup;
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

        actions.Controls.Add(_cancelButton);
        actions.Controls.Add(_exportButton);
        actions.Controls.Add(_previewButton);
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

        ExportFeatureType featureTypes =
            settings.FeatureTypes == ExportFeatureType.None ? ExportFeatureType.All : settings.FeatureTypes;
        _unitCheckBox.Checked = featureTypes.HasFlag(ExportFeatureType.Unit);
        _detailCheckBox.Checked = featureTypes.HasFlag(ExportFeatureType.Detail);
        _openingCheckBox.Checked = featureTypes.HasFlag(ExportFeatureType.Opening);
        _levelCheckBox.Checked = featureTypes.HasFlag(ExportFeatureType.Level);

        _targetEpsgTextBox.Text = settings.TargetEpsg > 0
            ? settings.TargetEpsg.ToString()
            : ProjectInfo.DefaultTargetEpsg.ToString();

        SelectPresetIfAvailable(settings.TargetEpsg);
        SelectLanguage(_language);
        ApplyLanguage();
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

        ExportFeatureType previewTypes = GetSelectedFeatureTypes() & (ExportFeatureType.Unit | ExportFeatureType.Opening);
        if (previewTypes == ExportFeatureType.None)
        {
            MessageBox.Show(
                this,
                UiLanguageText.Select(_language, "Preview requires unit and/or opening to be selected.", "プレビューには unit または opening の選択が必要です。"),
                ProjectInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _previewRequested(new ExportPreviewRequest(selectedViews, previewTypes, _language));
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
        ExportFeatureType previewTypes = GetSelectedFeatureTypes() & (ExportFeatureType.Unit | ExportFeatureType.Opening);
        _previewButton.Enabled = _previewRequested != null &&
                                 previewTypes != ExportFeatureType.None &&
                                 GetSelectedViews().Count > 0;
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
}
