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
    private readonly CheckedListBox _viewList = new();
    private readonly TextBox _outputDirectoryTextBox = new();
    private readonly ComboBox _crsPresetComboBox = new();
    private readonly TextBox _targetEpsgTextBox = new();
    private readonly CheckBox _unitCheckBox = new();
    private readonly CheckBox _detailCheckBox = new();
    private readonly CheckBox _openingCheckBox = new();
    private readonly CheckBox _levelCheckBox = new();
    private readonly CheckBox _splitByWallsCheckBox = new();

    private readonly IReadOnlyList<ViewSelectionItem> _viewItems;

    public ExportDialog(IReadOnlyList<ViewPlan> views, ExportDialogSettings settings)
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
        GroupBox group = new()
        {
            Dock = DockStyle.Fill,
            Text = "Plan Views",
        };

        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        group.Controls.Add(panel);

        _viewList.Dock = DockStyle.Fill;
        _viewList.CheckOnClick = true;
        _viewList.HorizontalScrollbar = true;
        _viewList.IntegralHeight = false;
        panel.Controls.Add(_viewList, 0, 0);

        FlowLayoutPanel viewActions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
        };

        Button selectAllButton = new()
        {
            Text = "Select All",
            Width = 100,
            Height = 28,
        };
        selectAllButton.Click += (_, _) =>
        {
            for (int i = 0; i < _viewList.Items.Count; i++)
            {
                _viewList.SetItemChecked(i, true);
            }
        };

        Button clearAllButton = new()
        {
            Text = "Clear All",
            Width = 100,
            Height = 28,
        };
        clearAllButton.Click += (_, _) =>
        {
            for (int i = 0; i < _viewList.Items.Count; i++)
            {
                _viewList.SetItemChecked(i, false);
            }
        };

        viewActions.Controls.Add(selectAllButton);
        viewActions.Controls.Add(clearAllButton);
        panel.Controls.Add(viewActions, 0, 1);

        return group;
    }

    private WinFormsControl BuildOptionsPanel()
    {
        GroupBox group = new()
        {
            Dock = DockStyle.Fill,
            Text = "Export Options",
        };

        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(10),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 120f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        group.Controls.Add(panel);

        panel.Controls.Add(new Label
        {
            Text = "Feature Types",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);

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
        featuresPanel.Controls.Add(_unitCheckBox);
        featuresPanel.Controls.Add(_detailCheckBox);
        featuresPanel.Controls.Add(_openingCheckBox);
        featuresPanel.Controls.Add(_levelCheckBox);
        panel.Controls.Add(featuresPanel, 0, 1);

        _splitByWallsCheckBox.Text = "Split floor units by walls";
        _splitByWallsCheckBox.Dock = DockStyle.Fill;
        panel.Controls.Add(_splitByWallsCheckBox, 0, 2);

        panel.Controls.Add(new Label
        {
            Text = "Output Directory",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 3);

        TableLayoutPanel outputPanel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
        };
        outputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92f));

        _outputDirectoryTextBox.Dock = DockStyle.Fill;
        outputPanel.Controls.Add(_outputDirectoryTextBox, 0, 0);

        Button browseButton = new()
        {
            Text = "Browse...",
            Width = 84,
            Height = 28,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
        };
        browseButton.Click += (_, _) =>
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
        outputPanel.Controls.Add(browseButton, 1, 0);
        panel.Controls.Add(outputPanel, 0, 4);

        panel.Controls.Add(new Label
        {
            Text = "CRS (EPSG)",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 5);

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

        panel.Controls.Add(crsPanel, 0, 6);
        return group;
    }

    private WinFormsControl BuildActionsPanel()
    {
        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10, 8, 10, 8),
        };

        Button cancelButton = new()
        {
            Text = "Cancel",
            Width = 90,
            Height = 30,
            DialogResult = DialogResult.Cancel,
        };

        Button exportButton = new()
        {
            Text = "Export",
            Width = 90,
            Height = 30,
        };
        exportButton.Click += (_, _) => ConfirmExport();

        actions.Controls.Add(cancelButton);
        actions.Controls.Add(exportButton);
        AcceptButton = exportButton;
        CancelButton = cancelButton;

        return actions;
    }

    private void LoadValues(ExportDialogSettings settings)
    {
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
        _splitByWallsCheckBox.Checked = settings.SplitUnitsByWalls;

        _targetEpsgTextBox.Text = settings.TargetEpsg > 0
            ? settings.TargetEpsg.ToString()
            : ProjectInfo.DefaultTargetEpsg.ToString();

        SelectPresetIfAvailable(settings.TargetEpsg);
    }

    private void ConfirmExport()
    {
        List<ViewPlan> selectedViews = GetSelectedViews();
        if (selectedViews.Count == 0)
        {
            MessageBox.Show(
                this,
                "Select at least one plan view to export.",
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
                "Select at least one feature type.",
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
                "Choose an output directory.",
                ProjectInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!int.TryParse(_targetEpsgTextBox.Text, out int epsg) || epsg <= 0)
        {
            MessageBox.Show(
                this,
                "Enter a valid EPSG code.",
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
            _splitByWallsCheckBox.Checked);
        DialogResult = DialogResult.OK;
        Close();
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
        int targetEpsg = int.TryParse(_targetEpsgTextBox.Text, out int epsg) ? epsg : ProjectInfo.DefaultTargetEpsg;
        return new ExportDialogSettings
        {
            OutputDirectory = (_outputDirectoryTextBox.Text ?? string.Empty).Trim(),
            TargetEpsg = targetEpsg,
            FeatureTypes = GetSelectedFeatureTypes(),
            SplitUnitsByWalls = _splitByWallsCheckBox.Checked,
            SelectedViewIds = GetSelectedViews().Select(x => x.Id.Value).ToList(),
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (string.IsNullOrWhiteSpace(_outputDirectoryTextBox.Text))
        {
            _outputDirectoryTextBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }

    private sealed class ViewSelectionItem
    {
        public ViewSelectionItem(ViewPlan view)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
        }

        public ViewPlan View { get; }

        public override string ToString()
        {
            string levelName = View.GenLevel?.Name ?? "<no level>";
            return $"{View.Name}  [Level: {levelName}]";
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
