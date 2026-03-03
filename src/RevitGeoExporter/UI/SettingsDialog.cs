using System;
using System.Drawing;
using System.Windows.Forms;
using RevitGeoExporter.Core.Coordinates;

namespace RevitGeoExporter.UI;

public sealed class SettingsDialog : Form
{
    private readonly ExportDialogSettings _original;
    private readonly TextBox _outputDirectoryTextBox = new();
    private readonly ComboBox _presetComboBox = new();
    private readonly TextBox _epsgTextBox = new();
    private readonly CheckBox _splitByWallsCheckBox = new();

    public SettingsDialog(ExportDialogSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        _original = settings;
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
            SplitUnitsByWalls = _splitByWallsCheckBox.Checked,
            SelectedViewIds = _original.SelectedViewIds ?? new System.Collections.Generic.List<long>(),
        };
    }

    private void InitializeComponents()
    {
        Text = "GeoExporter Settings";
        Width = 520;
        Height = 260;
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
            RowCount = 4,
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        root.Controls.Add(form, 0, 0);

        form.Controls.Add(new Label
        {
            Text = "Output Directory",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);

        TableLayoutPanel outputPanel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
        };
        outputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));

        _outputDirectoryTextBox.Dock = DockStyle.Fill;
        outputPanel.Controls.Add(_outputDirectoryTextBox, 0, 0);

        Button browseButton = new()
        {
            Text = "Browse...",
            Width = 88,
            Height = 28,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
        };
        browseButton.Click += (_, _) =>
        {
            using FolderBrowserDialog folderDialog = new()
            {
                Description = "Select default output directory",
                ShowNewFolderButton = true,
                SelectedPath = _outputDirectoryTextBox.Text,
            };

            if (folderDialog.ShowDialog(this) == DialogResult.OK)
            {
                _outputDirectoryTextBox.Text = folderDialog.SelectedPath;
            }
        };
        outputPanel.Controls.Add(browseButton, 1, 0);
        form.Controls.Add(outputPanel, 1, 0);

        form.Controls.Add(new Label
        {
            Text = "CRS Preset",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 1);

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
        form.Controls.Add(_presetComboBox, 1, 1);

        form.Controls.Add(new Label
        {
            Text = "Target EPSG",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 2);

        _epsgTextBox.Dock = DockStyle.Fill;
        form.Controls.Add(_epsgTextBox, 1, 2);

        _splitByWallsCheckBox.Text = "Split floor units by walls by default";
        _splitByWallsCheckBox.Dock = DockStyle.Fill;
        form.Controls.Add(_splitByWallsCheckBox, 0, 3);
        form.SetColumnSpan(_splitByWallsCheckBox, 2);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 6, 0, 0),
        };
        Button cancelButton = new()
        {
            Text = "Cancel",
            Width = 90,
            Height = 28,
            DialogResult = DialogResult.Cancel,
        };

        Button saveButton = new()
        {
            Text = "Save",
            Width = 90,
            Height = 28,
        };
        saveButton.Click += (_, _) =>
        {
            if (!int.TryParse(_epsgTextBox.Text, out int epsg) || epsg <= 0)
            {
                MessageBox.Show(
                    this,
                    "Enter a valid EPSG code.",
                    ProjectInfo.Name,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        actions.Controls.Add(cancelButton);
        actions.Controls.Add(saveButton);
        root.Controls.Add(actions, 0, 1);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private void LoadSettings(ExportDialogSettings settings)
    {
        _outputDirectoryTextBox.Text = settings.OutputDirectory ?? string.Empty;
        int epsg = settings.TargetEpsg > 0 ? settings.TargetEpsg : ProjectInfo.DefaultTargetEpsg;
        _epsgTextBox.Text = epsg.ToString();
        _splitByWallsCheckBox.Checked = settings.SplitUnitsByWalls;

        for (int i = 0; i < _presetComboBox.Items.Count; i++)
        {
            if (_presetComboBox.Items[i] is CrsPresetItem item && item.Epsg == epsg)
            {
                _presetComboBox.SelectedIndex = i;
                break;
            }
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
