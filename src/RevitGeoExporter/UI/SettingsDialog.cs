using System;
using System.Drawing;
using System.Windows.Forms;
using RevitGeoExporter.Core.Coordinates;

namespace RevitGeoExporter.UI;

public sealed class SettingsDialog : Form
{
    private readonly ExportDialogSettings _original;
    private readonly TextBox _outputDirectoryTextBox = new();
    private readonly ComboBox _languageComboBox = new();
    private readonly ComboBox _presetComboBox = new();
    private readonly TextBox _epsgTextBox = new();
    private readonly Label _languageLabel = new();
    private readonly Label _outputDirectoryLabel = new();
    private readonly Label _presetLabel = new();
    private readonly Label _epsgLabel = new();
    private readonly Button _browseButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _saveButton = new();
    private readonly Label _versionLabel = new();
    private UiLanguage _language;

    public SettingsDialog(ExportDialogSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        _original = settings;
        _language = Enum.IsDefined(typeof(UiLanguage), settings.UiLanguage)
            ? settings.UiLanguage
            : UiLanguage.English;

        InitializeComponents();
        LoadSettings(settings);
    }

    public ExportDialogSettings BuildSettings()
    {
        int epsg = int.TryParse(_epsgTextBox.Text, out int parsed)
            ? parsed
            : ProjectInfo.DefaultTargetEpsg;

        return new ExportDialogSettings
        {
            OutputDirectory = (_outputDirectoryTextBox.Text ?? string.Empty).Trim(),
            TargetEpsg = epsg,
            FeatureTypes = _original.FeatureTypes,
            SelectedViewIds = _original.SelectedViewIds ?? new System.Collections.Generic.List<long>(),
            GenerateDiagnosticsReport = _original.GenerateDiagnosticsReport,
            GeneratePackageOutput = _original.GeneratePackageOutput,
            IncludePackageLegend = _original.IncludePackageLegend,
            GeometryRepairOptions = _original.GeometryRepairOptions?.Clone() ?? new RevitGeoExporter.Core.Geometry.GeometryRepairOptions(),
            UiLanguage = _language,
            UnitSource = _original.UnitSource,
            RoomCategoryParameterName = _original.RoomCategoryParameterName,
        };
    }

    private void InitializeComponents()
    {
        Width = 520;
        Height = 300;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        Controls.Add(root);

        TableLayoutPanel form = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        root.Controls.Add(form, 0, 0);

        _languageLabel.Dock = DockStyle.Fill;
        _languageLabel.TextAlign = ContentAlignment.MiddleLeft;
        form.Controls.Add(_languageLabel, 0, 0);

        _languageComboBox.Dock = DockStyle.Fill;
        _languageComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageComboBox.Items.Add(new LanguageItem(UiLanguage.English));
        _languageComboBox.Items.Add(new LanguageItem(UiLanguage.Japanese));
        _languageComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (_languageComboBox.SelectedItem is LanguageItem selected)
            {
                _language = selected.Language;
                ApplyLanguage();
                UpdateVersionLabel();
            }
        };
        form.Controls.Add(_languageComboBox, 1, 0);

        _outputDirectoryLabel.Dock = DockStyle.Fill;
        _outputDirectoryLabel.TextAlign = ContentAlignment.MiddleLeft;
        form.Controls.Add(_outputDirectoryLabel, 0, 1);

        TableLayoutPanel outputPanel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
        };
        outputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));

        _outputDirectoryTextBox.Dock = DockStyle.Fill;
        outputPanel.Controls.Add(_outputDirectoryTextBox, 0, 0);

        _browseButton.Width = 88;
        _browseButton.Height = 28;
        _browseButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _browseButton.Click += (_, _) =>
        {
            using FolderBrowserDialog folderDialog = new()
            {
                Description = UiLanguageText.Get(_language, "SettingsDialog.SelectDefaultOutputDirectory", "Select default output directory"),
                ShowNewFolderButton = true,
                SelectedPath = _outputDirectoryTextBox.Text,
            };

            if (folderDialog.ShowDialog(this) == DialogResult.OK)
            {
                _outputDirectoryTextBox.Text = folderDialog.SelectedPath;
            }
        };
        outputPanel.Controls.Add(_browseButton, 1, 0);
        form.Controls.Add(outputPanel, 1, 1);

        _presetLabel.Dock = DockStyle.Fill;
        _presetLabel.TextAlign = ContentAlignment.MiddleLeft;
        form.Controls.Add(_presetLabel, 0, 2);

        _presetComboBox.Dock = DockStyle.Fill;
        _presetComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var zone in JapanPlaneRectangular.Zones)
        {
            _presetComboBox.Items.Add(new CrsPresetItem(zone.Key, zone.Value));
        }

        _presetComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (_presetComboBox.SelectedItem is CrsPresetItem selected)
            {
                _epsgTextBox.Text = selected.Epsg.ToString();
            }
        };
        form.Controls.Add(_presetComboBox, 1, 2);

        _epsgLabel.Dock = DockStyle.Fill;
        _epsgLabel.TextAlign = ContentAlignment.MiddleLeft;
        form.Controls.Add(_epsgLabel, 0, 3);

        _epsgTextBox.Dock = DockStyle.Fill;
        form.Controls.Add(_epsgTextBox, 1, 3);

        TableLayoutPanel actionsContainer = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(0, 6, 0, 0),
        };
        actionsContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        actionsContainer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _versionLabel.Dock = DockStyle.Fill;
        _versionLabel.TextAlign = ContentAlignment.MiddleLeft;
        actionsContainer.Controls.Add(_versionLabel, 0, 0);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
        };

        _cancelButton.Width = 90;
        _cancelButton.Height = 28;
        _cancelButton.DialogResult = DialogResult.Cancel;

        _saveButton.Width = 90;
        _saveButton.Height = 28;
        _saveButton.Click += (_, _) =>
        {
            if (!int.TryParse(_epsgTextBox.Text, out int epsg) || epsg <= 0)
            {
                MessageBox.Show(
                    this,
                    UiLanguageText.Get(_language, "ExportDialog.Message.EnterValidEpsg", "Enter a valid EPSG code."),
                    ProjectInfo.Name,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        actions.Controls.Add(_cancelButton);
        actions.Controls.Add(_saveButton);
        actionsContainer.Controls.Add(actions, 1, 0);
        root.Controls.Add(actionsContainer, 0, 1);

        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private void LoadSettings(ExportDialogSettings settings)
    {
        SelectLanguage(_language);
        _outputDirectoryTextBox.Text = settings.OutputDirectory ?? string.Empty;
        int epsg = settings.TargetEpsg > 0 ? settings.TargetEpsg : ProjectInfo.DefaultTargetEpsg;
        _epsgTextBox.Text = epsg.ToString();

        for (int i = 0; i < _presetComboBox.Items.Count; i++)
        {
            if (_presetComboBox.Items[i] is CrsPresetItem item && item.Epsg == epsg)
            {
                _presetComboBox.SelectedIndex = i;
                break;
            }
        }

        ApplyLanguage();
        UpdateVersionLabel();
    }

    private void ApplyLanguage()
    {
        Text = UiLanguageText.Get(_language, "SettingsHub.Title", "GeoExporter Settings");
        _languageLabel.Text = UiLanguageText.Get(_language, "Common.Language", "Language");
        _outputDirectoryLabel.Text = UiLanguageText.Get(_language, "Common.OutputDirectory", "Output Directory");
        _presetLabel.Text = UiLanguageText.Get(_language, "Common.CrsPreset", "CRS Preset");
        _epsgLabel.Text = UiLanguageText.Get(_language, "Common.TargetEpsg", "Target EPSG");
        _browseButton.Text = UiLanguageText.Get(_language, "Common.Browse", "Browse...");
        _cancelButton.Text = UiLanguageText.Get(_language, "Common.Cancel", "Cancel");
        _saveButton.Text = UiLanguageText.Get(_language, "Common.Save", "Save");
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
            ? $"バージョン {ProjectInfo.VersionTag}"
            : $"Version {ProjectInfo.VersionTag}";
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
        public CrsPresetItem(int epsg, string name)
        {
            Epsg = epsg;
            Name = name;
        }

        public int Epsg { get; }

        public string Name { get; }

        public override string ToString()
        {
            return $"EPSG:{Epsg} - {Name}";
        }
    }
}
