using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Help;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Resources;

namespace RevitGeoExporter.UI;

public sealed class SettingsHubForm : Form
{
    private readonly SettingsBundle _bundle;
    private readonly ZoneCatalog _zoneCatalog;
    private readonly string _projectKey;
    private readonly IReadOnlyList<string> _categories;
    private SettingsBundleSnapshot _snapshot;
    private UiLanguage _language;

    private readonly TabControl _tabs = new();
    private readonly ListBox _statusListBox = new();
    private readonly Label _profileSummaryLabel = new();
    private readonly TextBox _outputDirectoryTextBox = new();
    private readonly ComboBox _languageComboBox = new();
    private readonly ComboBox _presetComboBox = new();
    private readonly TextBox _epsgTextBox = new();
    private readonly CheckBox _diagnosticsCheckBox = new();
    private readonly CheckBox _packageCheckBox = new();
    private readonly CheckBox _packageLegendCheckBox = new();
    private readonly CheckBox _repairEnabledCheckBox = new();
    private readonly DataGridView _floorGrid = new();
    private readonly DataGridView _roomGrid = new();
    private readonly DataGridView _familyGrid = new();
    private readonly DataGridView _openingGrid = new();
    private readonly Button _saveButton = new();
    private readonly Button _closeButton = new();
    private readonly Button _helpButton = new();
    private readonly GroupBox _globalDefaultsGroup = new();
    private readonly GroupBox _statusGroup = new();

    public SettingsHubForm(
        string projectKey,
        SettingsBundle bundle,
        SettingsBundleSnapshot snapshot,
        ZoneCatalog zoneCatalog)
    {
        _projectKey = string.IsNullOrWhiteSpace(projectKey)
            ? throw new ArgumentException("A project key is required.", nameof(projectKey))
            : projectKey.Trim();
        _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _zoneCatalog = zoneCatalog ?? throw new ArgumentNullException(nameof(zoneCatalog));
        _categories = _zoneCatalog.GetKnownCategories().ToList();
        _language = _snapshot.GlobalSettings.UiLanguage;

        InitializeComponents();
        LoadSnapshot(_snapshot);
    }

    private void InitializeComponents()
    {
        Width = 1100;
        Height = 760;
        MinimumSize = new Size(920, 640);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
        Controls.Add(root);

        _tabs.Dock = DockStyle.Fill;
        _tabs.TabPages.Add(BuildGlobalTab());
        _tabs.TabPages.Add(BuildProjectTab());
        root.Controls.Add(_tabs, 0, 0);

        _statusGroup.Dock = DockStyle.Fill;
        _statusListBox.Dock = DockStyle.Fill;
        _statusListBox.HorizontalScrollbar = true;
        _statusGroup.Controls.Add(_statusListBox);
        root.Controls.Add(_statusGroup, 0, 1);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        _closeButton.Width = 96;
        _closeButton.Height = 28;
        _closeButton.DialogResult = DialogResult.Cancel;
        actions.Controls.Add(_closeButton);

        _helpButton.Width = 96;
        _helpButton.Height = 28;
        _helpButton.Click += (_, _) => HelpLauncher.Show(this, HelpTopic.SettingsAndProfiles, _language, Text);
        actions.Controls.Add(_helpButton);

        _saveButton.Width = 96;
        _saveButton.Height = 28;
        _saveButton.Click += (_, _) =>
        {
            SaveAll();
            DialogResult = DialogResult.OK;
            Close();
        };
        actions.Controls.Add(_saveButton);

        root.Controls.Add(actions, 0, 2);
        AcceptButton = _saveButton;
        CancelButton = _closeButton;
    }

    private TabPage BuildGlobalTab()
    {
        TabPage tab = new();
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(6),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        tab.Controls.Add(panel);

        _globalDefaultsGroup.Dock = DockStyle.Fill;
        _globalDefaultsGroup.Controls.Add(BuildGlobalDefaultsPanel());
        panel.Controls.Add(_globalDefaultsGroup, 0, 0);

        _profileSummaryLabel.Dock = DockStyle.Fill;
        _profileSummaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_profileSummaryLabel, 0, 1);

        panel.Controls.Add(BuildScopeButtons(SettingsScope.Global), 0, 2);
        return tab;
    }

    private Control BuildGlobalDefaultsPanel()
    {
        TableLayoutPanel form = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(8),
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180f));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        AddFormRow(form, 0, () => L("Common.Language", "Language"), BuildLanguagePicker());
        AddFormRow(form, 1, () => L("Common.OutputDirectory", "Output Directory"), BuildOutputDirectoryPanel());
        AddFormRow(form, 2, () => L("Common.CrsPreset", "CRS Preset"), BuildPresetPicker());
        AddFormRow(form, 3, () => L("Common.TargetEpsg", "Target EPSG"), _epsgTextBox);

        _diagnosticsCheckBox.AutoSize = true;
        _packageCheckBox.AutoSize = true;
        _packageLegendCheckBox.AutoSize = true;
        _repairEnabledCheckBox.AutoSize = true;
        _packageCheckBox.CheckedChanged += (_, _) => _packageLegendCheckBox.Enabled = _packageCheckBox.Checked;

        FlowLayoutPanel toggles = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
        };
        toggles.Controls.Add(_diagnosticsCheckBox);
        toggles.Controls.Add(_packageCheckBox);
        toggles.Controls.Add(_packageLegendCheckBox);
        toggles.Controls.Add(_repairEnabledCheckBox);
        AddFormRow(form, 4, () => L("ExportDialog.Options", "Export Options"), toggles);

        return form;
    }

    private TabPage BuildProjectTab()
    {
        TabPage tab = new();
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(6),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        tab.Controls.Add(panel);

        Label intro = new()
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        panel.Controls.Add(intro, 0, 0);

        TabControl mappingTabs = new()
        {
            Dock = DockStyle.Fill,
        };
        TabPage floorTab = new();
        TabPage roomTab = new();
        TabPage familyTab = new();
        TabPage openingTab = new();
        ConfigureMappingGrid(_floorGrid);
        ConfigureMappingGrid(_roomGrid);
        ConfigureMappingGrid(_familyGrid);
        ConfigureOpeningGrid(_openingGrid);
        floorTab.Controls.Add(_floorGrid);
        roomTab.Controls.Add(_roomGrid);
        familyTab.Controls.Add(_familyGrid);
        openingTab.Controls.Add(_openingGrid);
        mappingTabs.TabPages.Add(floorTab);
        mappingTabs.TabPages.Add(roomTab);
        mappingTabs.TabPages.Add(familyTab);
        mappingTabs.TabPages.Add(openingTab);
        panel.Controls.Add(mappingTabs, 0, 1);

        panel.Controls.Add(BuildScopeButtons(SettingsScope.Project), 0, 2);

        tab.Tag = new object[] { intro, mappingTabs, floorTab, roomTab, familyTab, openingTab };
        return tab;
    }

    private Control BuildScopeButtons(SettingsScope scope)
    {
        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };

        Button importButton = new() { Width = 108, Height = 28 };
        importButton.Click += (_, _) => ImportScope(scope);
        actions.Controls.Add(importButton);

        Button exportButton = new() { Width = 108, Height = 28 };
        exportButton.Click += (_, _) => ExportScope(scope);
        actions.Controls.Add(exportButton);

        Button resetButton = new() { Width = 136, Height = 28 };
        resetButton.Click += (_, _) => ResetScope(scope);
        actions.Controls.Add(resetButton);

        actions.Tag = new ScopeButtonSet(importButton, exportButton, resetButton, scope);
        return actions;
    }

    private Control BuildLanguagePicker()
    {
        _languageComboBox.Dock = DockStyle.Fill;
        _languageComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageComboBox.Items.Add(new LanguageItem(UiLanguage.English));
        _languageComboBox.Items.Add(new LanguageItem(UiLanguage.Japanese));
        _languageComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (_languageComboBox.SelectedItem is LanguageItem item)
            {
                _language = item.Language;
                ApplyLanguage();
            }
        };
        return _languageComboBox;
    }

    private Control BuildPresetPicker()
    {
        _presetComboBox.Dock = DockStyle.Fill;
        _presetComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (KeyValuePair<int, string> zone in JapanPlaneRectangular.Zones.OrderBy(entry => entry.Key))
        {
            _presetComboBox.Items.Add(new CrsPresetItem(zone.Key, zone.Value));
        }

        _presetComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (_presetComboBox.SelectedItem is CrsPresetItem item)
            {
                _epsgTextBox.Text = item.Epsg.ToString();
            }
        };

        return _presetComboBox;
    }

    private Control BuildOutputDirectoryPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
        _outputDirectoryTextBox.Dock = DockStyle.Fill;
        panel.Controls.Add(_outputDirectoryTextBox, 0, 0);

        Button browseButton = new() { Dock = DockStyle.Fill };
        browseButton.Click += (_, _) =>
        {
            using FolderBrowserDialog dialog = new()
            {
                ShowNewFolderButton = true,
                SelectedPath = _outputDirectoryTextBox.Text,
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _outputDirectoryTextBox.Text = dialog.SelectedPath;
            }
        };
        panel.Controls.Add(browseButton, 1, 0);
        panel.Tag = browseButton;
        return panel;
    }

    private static void AddFormRow(TableLayoutPanel panel, int rowIndex, Func<string> labelFactory, Control control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, rowIndex == 4 ? 120f : 32f));
        Label label = new()
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = labelFactory(),
        };
        label.Tag = labelFactory;
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(label, 0, rowIndex);
        panel.Controls.Add(control, 1, rowIndex);
    }

    private void ConfigureMappingGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.AutoGenerateColumns = false;
        grid.AllowUserToAddRows = true;
        grid.AllowUserToDeleteRows = true;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns.Clear();
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Key",
            FillWeight = 55f,
        });
        DataGridViewComboBoxColumn categoryColumn = new()
        {
            Name = "Category",
            FillWeight = 45f,
            FlatStyle = FlatStyle.Flat,
        };
        foreach (string category in _categories)
        {
            categoryColumn.Items.Add(category);
        }

        grid.Columns.Add(categoryColumn);
    }

    private static void ConfigureOpeningGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.AutoGenerateColumns = false;
        grid.AllowUserToAddRows = true;
        grid.AllowUserToDeleteRows = true;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns.Clear();
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FamilyName",
            FillWeight = 100f,
        });
    }

    private void LoadSnapshot(SettingsBundleSnapshot snapshot)
    {
        _snapshot = snapshot;
        SelectLanguage(snapshot.GlobalSettings.UiLanguage);
        _outputDirectoryTextBox.Text = snapshot.GlobalSettings.OutputDirectory ?? string.Empty;
        _epsgTextBox.Text = snapshot.GlobalSettings.TargetEpsg.ToString();
        _diagnosticsCheckBox.Checked = snapshot.GlobalSettings.GenerateDiagnosticsReport;
        _packageCheckBox.Checked = snapshot.GlobalSettings.GeneratePackageOutput;
        _packageLegendCheckBox.Checked = snapshot.GlobalSettings.IncludePackageLegend;
        _packageLegendCheckBox.Enabled = _packageCheckBox.Checked;
        _repairEnabledCheckBox.Checked = snapshot.GlobalSettings.GeometryRepairOptions?.Enabled ?? false;
        SelectPreset(snapshot.GlobalSettings.TargetEpsg);
        PopulateMappings(snapshot.ProjectMappings);
        PopulateStatuses(snapshot.StatusEntries);
        ApplyLanguage();
    }

    private void PopulateMappings(ProjectMappingRules rules)
    {
        PopulateMappingGrid(_floorGrid, rules.FloorCategoryOverrides);
        PopulateMappingGrid(_roomGrid, rules.RoomCategoryOverrides);
        PopulateMappingGrid(_familyGrid, rules.FamilyCategoryOverrides);
        _openingGrid.Rows.Clear();
        foreach (string familyName in rules.AcceptedOpeningFamilies)
        {
            _openingGrid.Rows.Add(familyName);
        }
    }

    private static void PopulateMappingGrid(DataGridView grid, IReadOnlyDictionary<string, string> mappings)
    {
        grid.Rows.Clear();
        foreach (KeyValuePair<string, string> entry in mappings.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            grid.Rows.Add(entry.Key, entry.Value);
        }
    }

    private void PopulateStatuses(IReadOnlyList<SettingsStatusEntry> statuses)
    {
        _statusListBox.Items.Clear();
        if (statuses == null || statuses.Count == 0)
        {
            _statusListBox.Items.Add("-");
            return;
        }

        foreach (SettingsStatusEntry status in statuses)
        {
            _statusListBox.Items.Add($"[{status.Scope}] {status.Message}");
        }
    }

    private void SaveAll()
    {
        ExportDialogSettings settings = BuildGlobalSettings();
        _bundle.SaveGlobalSettings(settings);
        _bundle.SaveProjectMappings(BuildProjectMappings());
        _snapshot = _bundle.Load();
        LoadSnapshot(_snapshot);

        MessageBox.Show(
            this,
            L("SettingsHub.ScopeSaved.Global", "Global settings were saved."),
            ProjectInfo.Name,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ImportScope(SettingsScope scope)
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = scope == SettingsScope.Global
                ? L("SettingsHub.ImportTitle.Global", "Import Global Settings Bundle")
                : L("SettingsHub.ImportTitle.Project", "Import Project Settings Bundle"),
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        SettingsImportResult result = _bundle.ImportScope(scope, dialog.FileName);
        _snapshot = _bundle.Load();
        LoadSnapshot(new SettingsBundleSnapshot
        {
            GlobalSettings = _snapshot.GlobalSettings,
            Profiles = _snapshot.Profiles,
            ProjectMappings = _snapshot.ProjectMappings,
            StatusEntries = _snapshot.StatusEntries.Concat(result.Statuses).ToList(),
        });

        if (result.Succeeded)
        {
            MessageBox.Show(
                this,
                scope == SettingsScope.Global
                    ? L("SettingsHub.ImportSucceeded.Global", "Imported global settings bundle.")
                    : L("SettingsHub.ImportSucceeded.Project", "Imported project settings bundle."),
                ProjectInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private void ExportScope(SettingsScope scope)
    {
        using SaveFileDialog dialog = new()
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
            AddExtension = true,
            FileName = scope == SettingsScope.Global ? "global-settings.json" : "project-settings.json",
            Title = scope == SettingsScope.Global
                ? L("SettingsHub.ExportTitle.Global", "Export Global Settings Bundle")
                : L("SettingsHub.ExportTitle.Project", "Export Project Settings Bundle"),
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        _bundle.ExportScope(scope, CurrentSnapshot(), dialog.FileName);
    }

    private void ResetScope(SettingsScope scope)
    {
        string message = scope == SettingsScope.Global
            ? L("SettingsHub.ResetConfirm.Global", "Reset global defaults and remove all global export profiles?")
            : L("SettingsHub.ResetConfirm.Project", "Clear project mappings and remove all project-scoped export profiles?");

        if (MessageBox.Show(this, message, ProjectInfo.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
        {
            return;
        }

        _bundle.ResetScope(scope);
        _snapshot = _bundle.Load();
        LoadSnapshot(_snapshot);
    }

    private SettingsBundleSnapshot CurrentSnapshot()
    {
        return new SettingsBundleSnapshot
        {
            GlobalSettings = BuildGlobalSettings(),
            Profiles = _snapshot.Profiles,
            ProjectMappings = BuildProjectMappings(),
            StatusEntries = _snapshot.StatusEntries,
        };
    }

    private ExportDialogSettings BuildGlobalSettings()
    {
        int targetEpsg = int.TryParse(_epsgTextBox.Text, out int epsg)
            ? epsg
            : ProjectInfo.DefaultTargetEpsg;
        ExportDialogSettings settings = _snapshot.GlobalSettings;
        settings.OutputDirectory = (_outputDirectoryTextBox.Text ?? string.Empty).Trim();
        settings.TargetEpsg = targetEpsg;
        settings.GenerateDiagnosticsReport = _diagnosticsCheckBox.Checked;
        settings.GeneratePackageOutput = _packageCheckBox.Checked;
        settings.IncludePackageLegend = _packageLegendCheckBox.Checked;
        settings.UiLanguage = _language;
        settings.GeometryRepairOptions.Enabled = _repairEnabledCheckBox.Checked;
        return settings;
    }

    private ProjectMappingRules BuildProjectMappings()
    {
        return ProjectMappingRules.Create(ReadMappings(_floorGrid), ReadMappings(_roomGrid), ReadMappings(_familyGrid), ReadFamilies(_openingGrid));
    }

    private static Dictionary<string, string> ReadMappings(DataGridView grid)
    {
        Dictionary<string, string> mappings = new(StringComparer.Ordinal);
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            string key = row.Cells[0].Value?.ToString()?.Trim() ?? string.Empty;
            string category = row.Cells[1].Value?.ToString()?.Trim() ?? string.Empty;
            if (key.Length > 0 && category.Length > 0)
            {
                mappings[key] = category;
            }
        }

        return mappings;
    }

    private static List<string> ReadFamilies(DataGridView grid)
    {
        return grid.Rows.Cast<DataGridViewRow>()
            .Where(row => !row.IsNewRow)
            .Select(row => row.Cells[0].Value?.ToString()?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ApplyLanguage()
    {
        Text = L("SettingsHub.Title", "GeoExporter Settings");
        _tabs.TabPages[0].Text = L("SettingsHub.GlobalTab", "Global");
        _tabs.TabPages[1].Text = L("SettingsHub.ProjectTab", "Project");
        _globalDefaultsGroup.Text = L("SettingsHub.DefaultsGroup", "Default Export Settings");
        _statusGroup.Text = L("SettingsHub.ScopeStatusGroup", "Configuration Status");
        _saveButton.Text = L("Common.Save", "Save");
        _helpButton.Text = L("Common.Help", "Help");
        _closeButton.Text = L("Common.Close", "Close");
        _profileSummaryLabel.Text = string.Format(
            L("SettingsHub.ProfileSummary", "Global profiles: {0}    Project profiles: {1}"),
            _snapshot.Profiles.Count(profile => profile.Scope == ExportProfileScope.Global),
            _snapshot.Profiles.Count(profile => profile.Scope == ExportProfileScope.Project));

        UpdateLabelsRecursive(this.Controls);

        if (_tabs.TabPages[1].Tag is object[] projectObjects)
        {
            ((Label)projectObjects[0]).Text = L("SettingsHub.ProjectMappingsIntro", "Manage exporter-side floor, room, family, and accepted opening mappings for the current project.");
            TabControl mappingTabs = (TabControl)projectObjects[1];
            mappingTabs.TabPages[0].Text = L("SettingsHub.FloorOverridesTab", "Floor Overrides");
            mappingTabs.TabPages[1].Text = "Room Categories";
            mappingTabs.TabPages[2].Text = L("SettingsHub.FamilyCategoriesTab", "Family Categories");
            mappingTabs.TabPages[3].Text = L("SettingsHub.AcceptedOpeningsTab", "Accepted Openings");
        }

        _floorGrid.Columns[0].HeaderText = L("SettingsHub.FloorTypeName", "Floor Type Name");
        _floorGrid.Columns[1].HeaderText = L("SettingsHub.Category", "Category");
        _roomGrid.Columns[0].HeaderText = "Room Value";
        _roomGrid.Columns[1].HeaderText = L("SettingsHub.Category", "Category");
        _familyGrid.Columns[0].HeaderText = L("SettingsHub.FamilyName", "Family Name");
        _familyGrid.Columns[1].HeaderText = L("SettingsHub.Category", "Category");
        _openingGrid.Columns[0].HeaderText = L("SettingsHub.AcceptedOpeningFamilyName", "Accepted Opening Family Name");
        _diagnosticsCheckBox.Text = L("ExportDialog.WriteDiagnostics", "Write diagnostics report");
        _packageCheckBox.Text = L("ExportDialog.WritePackage", "Write GIS package");
        _packageLegendCheckBox.Text = L("ExportDialog.IncludeLegend", "Include legend file");
        _repairEnabledCheckBox.Text = L("ExportDialog.GeometryRepair", "Geometry Repair");
    }

    private void UpdateLabelsRecursive(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            if (control is Label label && label.Tag is Func<string> labelFactory)
            {
                label.Text = labelFactory();
            }

            if (control.Tag is ScopeButtonSet set)
            {
                set.ImportButton.Text = L("Common.Import", "Import...");
                set.ExportButton.Text = L("Common.Export", "Export...");
                set.ResetButton.Text = set.Scope == SettingsScope.Global
                    ? L("Common.ResetDefaults", "Reset Defaults")
                    : L("Common.ClearProjectData", "Clear Project Data");
            }

            if (control is TableLayoutPanel layout && layout.Tag is Button browseButton)
            {
                browseButton.Text = L("Common.Browse", "Browse...");
            }

            UpdateLabelsRecursive(control.Controls);
        }
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

    private void SelectPreset(int epsg)
    {
        for (int i = 0; i < _presetComboBox.Items.Count; i++)
        {
            if (_presetComboBox.Items[i] is CrsPresetItem item && item.Epsg == epsg)
            {
                _presetComboBox.SelectedIndex = i;
                return;
            }
        }

        _presetComboBox.SelectedIndex = -1;
    }

    private string L(string key, string fallback)
    {
        return LocalizedTextProvider.Get(_language, key, fallback);
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

    private sealed class ScopeButtonSet
    {
        public ScopeButtonSet(Button importButton, Button exportButton, Button resetButton, SettingsScope scope)
        {
            ImportButton = importButton;
            ExportButton = exportButton;
            ResetButton = resetButton;
            Scope = scope;
        }

        public Button ImportButton { get; }

        public Button ExportButton { get; }

        public Button ResetButton { get; }

        public SettingsScope Scope { get; }
    }
}
