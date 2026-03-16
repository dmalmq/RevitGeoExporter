using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RevitGeoExporter.Core.Assignments;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Help;
using RevitGeoExporter.Resources;
using WinForms = System.Windows.Forms;

namespace RevitGeoExporter.UI;

public sealed class SettingsHubForm : IDisposable
{
    private readonly SettingsBundle _bundle;
    private readonly ZoneCatalog _zoneCatalog;
    private readonly IReadOnlyList<string> _categories;
    private SettingsBundleSnapshot _snapshot;
    private UiLanguage _language;

    private readonly Window _window;
    private readonly TabControl _tabs = new();
    private readonly GroupBox _statusGroup = new();
    private readonly ListBox _statusList = new();
    private readonly TextBlock _profileSummary = new();
    private readonly TextBox _outputDirectoryTextBox = new();
    private readonly TextBox _basemapUrlTemplateTextBox = new();
    private readonly TextBox _basemapAttributionTextBox = new();
    private readonly Button _browseOutputDirectoryButton = new();
    private readonly ComboBox _languageComboBox = new();
    private readonly ComboBox _presetComboBox = new();
    private readonly TextBox _epsgTextBox = new();
    private readonly CheckBox _diagnosticsCheckBox = new();
    private readonly CheckBox _packageCheckBox = new();
    private readonly CheckBox _packageLegendCheckBox = new();
    private readonly CheckBox _repairEnabledCheckBox = new();
    private readonly DataGrid _floorGrid = new();
    private readonly DataGrid _roomGrid = new();
    private readonly DataGrid _familyGrid = new();
    private readonly DataGrid _openingGrid = new();

    private readonly ObservableCollection<MappingRow> _floorRows = new();
    private readonly ObservableCollection<MappingRow> _roomRows = new();
    private readonly ObservableCollection<MappingRow> _familyRows = new();
    private readonly ObservableCollection<OpeningRow> _openingRows = new();

    private readonly Dictionary<SettingsScope, ScopeButtonSet> _scopeButtons = new();

    private readonly Button _saveButton = new();
    private readonly Button _closeButton = new();
    private readonly Button _helpButton = new();

    public SettingsHubForm(string projectKey, SettingsBundle bundle, SettingsBundleSnapshot snapshot, ZoneCatalog zoneCatalog)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
        {
            throw new ArgumentException("A project key is required.", nameof(projectKey));
        }
        _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
        _snapshot = NormalizeSnapshot(snapshot ?? throw new ArgumentNullException(nameof(snapshot)));
        _zoneCatalog = zoneCatalog ?? throw new ArgumentNullException(nameof(zoneCatalog));
        _categories = _zoneCatalog.GetKnownCategories().ToList();
        _language = _snapshot.GlobalSettings.UiLanguage;

        _window = new Window
        {
            Width = 1100,
            Height = 760,
            MinWidth = 920,
            MinHeight = 640,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = BuildLayout(),
        };

        LoadSnapshot(_snapshot);
    }

    public WinForms.DialogResult ShowDialog()
    {
        bool? result = _window.ShowDialog();
        return result == true ? WinForms.DialogResult.OK : WinForms.DialogResult.Cancel;
    }

    public void Dispose()
    {
        if (_window.IsVisible)
        {
            _window.Close();
        }
    }

    private UIElement BuildLayout()
    {
        Grid root = new() { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(120) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _tabs.Items.Add(BuildGlobalTab());
        _tabs.Items.Add(BuildProjectTab());
        root.Children.Add(_tabs);

        Grid.SetRow(_statusGroup, 1);
        _statusList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _statusGroup.Content = _statusList;
        root.Children.Add(_statusGroup);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        _closeButton.Width = 96;
        _closeButton.Click += (_, _) => { _window.DialogResult = false; _window.Close(); };
        _helpButton.Width = 96;
        _helpButton.Margin = new Thickness(8, 0, 0, 0);
        _helpButton.Click += (_, _) => HelpLauncher.Show(null, HelpTopic.SettingsAndProfiles, _language, _window.Title);
        _saveButton.Width = 96;
        _saveButton.Margin = new Thickness(8, 0, 0, 0);
        _saveButton.Click += (_, _) => SaveAll();

        actions.Children.Add(_helpButton);
        actions.Children.Add(_closeButton);
        actions.Children.Add(_saveButton);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        _packageCheckBox.Checked += (_, _) => _packageLegendCheckBox.IsEnabled = true;
        _packageCheckBox.Unchecked += (_, _) => _packageLegendCheckBox.IsEnabled = false;

        ApplyLanguage();
        return root;
    }

    private TabItem BuildGlobalTab()
    {
        Grid panel = new() { Margin = new Thickness(6) };
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        GroupBox defaultsGroup = new();
        defaultsGroup.Content = BuildGlobalDefaultsPanel();
        panel.Children.Add(defaultsGroup);

        _profileSummary.Margin = new Thickness(0, 8, 0, 8);
        Grid.SetRow(_profileSummary, 1);
        panel.Children.Add(_profileSummary);

        ScopeButtonSet globalButtons = BuildScopeButtons(SettingsScope.Global);
        Grid.SetRow(globalButtons.Container, 2);
        panel.Children.Add(globalButtons.Container);

        TabItem tab = new()
        {
            Content = panel,
            Tag = defaultsGroup,
        };

        return tab;
    }

    private UIElement BuildGlobalDefaultsPanel()
    {
        Grid form = new() { Margin = new Thickness(8) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 7; i++)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = i == 6 ? new GridLength(120) : GridLength.Auto });
        }

        AddFormRow(form, 0, () => L("Common.Language", "Language"), BuildLanguagePicker());
        AddFormRow(form, 1, () => L("Common.OutputDirectory", "Output Directory"), BuildOutputDirectoryPanel());
        AddFormRow(form, 2, () => L("SettingsHub.BasemapSource", "Basemap source"), _basemapUrlTemplateTextBox);
        AddFormRow(form, 3, () => L("SettingsHub.BasemapAttribution", "Basemap attribution"), _basemapAttributionTextBox);
        AddFormRow(form, 4, () => L("Common.CrsPreset", "CRS Preset"), BuildPresetPicker());
        AddFormRow(form, 5, () => L("Common.TargetEpsg", "Target EPSG"), _epsgTextBox);

        StackPanel toggles = new() { Orientation = Orientation.Vertical };
        toggles.Children.Add(_diagnosticsCheckBox);
        toggles.Children.Add(_packageCheckBox);
        toggles.Children.Add(_packageLegendCheckBox);
        toggles.Children.Add(_repairEnabledCheckBox);
        AddFormRow(form, 6, () => L("ExportDialog.Options", "Export Options"), toggles);

        return form;
    }
    private TabItem BuildProjectTab()
    {
        Grid panel = new() { Margin = new Thickness(6) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock intro = new();
        panel.Children.Add(intro);

        TabControl mappingTabs = new() { Margin = new Thickness(0, 8, 0, 8) };
        ConfigureMappingGrid(_floorGrid, _floorRows);
        ConfigureMappingGrid(_roomGrid, _roomRows);
        ConfigureMappingGrid(_familyGrid, _familyRows);
        ConfigureOpeningGrid(_openingGrid, _openingRows);

        mappingTabs.Items.Add(new TabItem { Content = _floorGrid });
        mappingTabs.Items.Add(new TabItem { Content = _roomGrid });
        mappingTabs.Items.Add(new TabItem { Content = _familyGrid });
        mappingTabs.Items.Add(new TabItem { Content = _openingGrid });

        Grid.SetRow(mappingTabs, 1);
        panel.Children.Add(mappingTabs);

        ScopeButtonSet projectButtons = BuildScopeButtons(SettingsScope.Project);
        Grid.SetRow(projectButtons.Container, 2);
        panel.Children.Add(projectButtons.Container);

        return new TabItem
        {
            Content = panel,
            Tag = new ProjectTabRefs(intro, mappingTabs),
        };
    }

    private ScopeButtonSet BuildScopeButtons(SettingsScope scope)
    {
        StackPanel actions = new() { Orientation = Orientation.Horizontal };
        Button importButton = new() { Width = 108, Height = 28 };
        importButton.Click += (_, _) => ImportScope(scope);
        Button exportButton = new() { Width = 108, Height = 28, Margin = new Thickness(8, 0, 0, 0) };
        exportButton.Click += (_, _) => ExportScope(scope);
        Button resetButton = new() { Width = 136, Height = 28, Margin = new Thickness(8, 0, 0, 0) };
        resetButton.Click += (_, _) => ResetScope(scope);
        actions.Children.Add(importButton);
        actions.Children.Add(exportButton);
        actions.Children.Add(resetButton);

        ScopeButtonSet set = new(importButton, exportButton, resetButton, actions, scope);
        _scopeButtons[scope] = set;
        return set;
    }

    private UIElement BuildLanguagePicker()
    {
        _languageComboBox.Items.Add(new LanguageItem(UiLanguage.English));
        _languageComboBox.Items.Add(new LanguageItem(UiLanguage.Japanese));
        _languageComboBox.SelectionChanged += (_, _) =>
        {
            if (_languageComboBox.SelectedItem is LanguageItem item)
            {
                _language = item.Language;
                ApplyLanguage();
            }
        };
        return _languageComboBox;
    }

    private UIElement BuildPresetPicker()
    {
        foreach (KeyValuePair<int, string> zone in JapanPlaneRectangular.Zones.OrderBy(entry => entry.Key))
        {
            _presetComboBox.Items.Add(new CrsPresetItem(zone.Key, zone.Value));
        }

        _presetComboBox.SelectionChanged += (_, _) =>
        {
            if (_presetComboBox.SelectedItem is CrsPresetItem item)
            {
                _epsgTextBox.Text = item.Epsg.ToString();
            }
        };

        return _presetComboBox;
    }

    private UIElement BuildOutputDirectoryPanel()
    {
        Grid panel = new();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.Children.Add(_outputDirectoryTextBox);

        _browseOutputDirectoryButton.Width = 100;
        _browseOutputDirectoryButton.Margin = new Thickness(8, 0, 0, 0);
        _browseOutputDirectoryButton.Click += (_, _) =>
        {
            using WinForms.FolderBrowserDialog dialog = new()
            {
                ShowNewFolderButton = true,
                SelectedPath = _outputDirectoryTextBox.Text,
            };

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                _outputDirectoryTextBox.Text = dialog.SelectedPath;
            }
        };

        Grid.SetColumn(_browseOutputDirectoryButton, 1);
        panel.Children.Add(_browseOutputDirectoryButton);
        return panel;
    }

    private static void AddFormRow(Grid panel, int rowIndex, Func<string> labelFactory, UIElement control)
    {
        TextBlock label = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Text = labelFactory(),
            Tag = labelFactory,
        };
        Grid.SetRow(label, rowIndex);
        panel.Children.Add(label);

        Grid.SetRow(control, rowIndex);
        Grid.SetColumn(control, 1);
        panel.Children.Add(control);
    }

    private void ConfigureMappingGrid(DataGrid grid, ObservableCollection<MappingRow> source)
    {
        grid.AutoGenerateColumns = false;
        grid.CanUserAddRows = true;
        grid.CanUserDeleteRows = true;
        grid.ItemsSource = source;

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = string.Empty,
            Binding = new System.Windows.Data.Binding(nameof(MappingRow.Key)),
            Width = new DataGridLength(0.55, DataGridLengthUnitType.Star),
        });

        grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = string.Empty,
            ItemsSource = _categories,
            SelectedItemBinding = new System.Windows.Data.Binding(nameof(MappingRow.Category)),
            Width = new DataGridLength(0.45, DataGridLengthUnitType.Star),
        });
    }

    private static void ConfigureOpeningGrid(DataGrid grid, ObservableCollection<OpeningRow> source)
    {
        grid.AutoGenerateColumns = false;
        grid.CanUserAddRows = true;
        grid.CanUserDeleteRows = true;
        grid.ItemsSource = source;
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = string.Empty,
            Binding = new System.Windows.Data.Binding(nameof(OpeningRow.FamilyName)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
    }

    private void LoadSnapshot(SettingsBundleSnapshot snapshot)
    {
        _snapshot = NormalizeSnapshot(snapshot);
        SelectLanguage(_snapshot.GlobalSettings.UiLanguage);
        _outputDirectoryTextBox.Text = _snapshot.GlobalSettings.OutputDirectory ?? string.Empty;
        _basemapUrlTemplateTextBox.Text = _snapshot.GlobalSettings.PreviewBasemapUrlTemplate ?? string.Empty;
        _basemapAttributionTextBox.Text = _snapshot.GlobalSettings.PreviewBasemapAttribution ?? string.Empty;
        _epsgTextBox.Text = _snapshot.GlobalSettings.TargetEpsg.ToString();
        _diagnosticsCheckBox.IsChecked = _snapshot.GlobalSettings.GenerateDiagnosticsReport;
        _packageCheckBox.IsChecked = _snapshot.GlobalSettings.GeneratePackageOutput;
        _packageLegendCheckBox.IsChecked = _snapshot.GlobalSettings.IncludePackageLegend;
        _packageLegendCheckBox.IsEnabled = _packageCheckBox.IsChecked == true;
        _repairEnabledCheckBox.IsChecked = _snapshot.GlobalSettings.GeometryRepairOptions?.Enabled ?? false;
        _language = _snapshot.GlobalSettings.UiLanguage;
        SelectPreset(_snapshot.GlobalSettings.TargetEpsg);
        PopulateMappings(_snapshot.ProjectMappings);
        PopulateStatuses(_snapshot.StatusEntries);
        ApplyLanguage();
    }

    private void PopulateMappings(ProjectMappingRules rules)
    {
        PopulateMappingRows(_floorRows, rules.FloorCategoryOverrides);
        PopulateMappingRows(_roomRows, rules.RoomCategoryOverrides);
        PopulateMappingRows(_familyRows, rules.FamilyCategoryOverrides);

        _openingRows.Clear();
        foreach (string familyName in rules.AcceptedOpeningFamilies)
        {
            _openingRows.Add(new OpeningRow { FamilyName = familyName });
        }
    }

    private static void PopulateMappingRows(ObservableCollection<MappingRow> rows, IReadOnlyDictionary<string, string> mappings)
    {
        rows.Clear();
        foreach (KeyValuePair<string, string> entry in mappings.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new MappingRow { Key = entry.Key, Category = entry.Value });
        }
    }

    private void PopulateStatuses(IReadOnlyList<SettingsStatusEntry> statuses)
    {
        _statusList.Items.Clear();
        if (statuses == null || statuses.Count == 0)
        {
            _statusList.Items.Add("-");
            return;
        }

        foreach (SettingsStatusEntry status in statuses)
        {
            _statusList.Items.Add($"[{status.Scope}] {status.Message}");
        }
    }

    private void SaveAll()
    {
        _bundle.SaveGlobalSettings(BuildGlobalSettings());
        _bundle.SaveProjectMappings(BuildProjectMappings());
        _snapshot = _bundle.Load();
        LoadSnapshot(_snapshot);

        System.Windows.MessageBox.Show(
            L("SettingsHub.ScopeSaved.Global", "Global settings were saved."),
            ProjectInfo.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        _window.DialogResult = true;
        _window.Close();
    }

    private void ImportScope(SettingsScope scope)
    {
        using WinForms.OpenFileDialog dialog = new()
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = scope == SettingsScope.Global
                ? L("SettingsHub.ImportTitle.Global", "Import Global Settings Bundle")
                : L("SettingsHub.ImportTitle.Project", "Import Project Settings Bundle"),
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
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
            System.Windows.MessageBox.Show(
                scope == SettingsScope.Global
                    ? L("SettingsHub.ImportSucceeded.Global", "Imported global settings bundle.")
                    : L("SettingsHub.ImportSucceeded.Project", "Imported project settings bundle."),
                ProjectInfo.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void ExportScope(SettingsScope scope)
    {
        using WinForms.SaveFileDialog dialog = new()
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
            AddExtension = true,
            FileName = scope == SettingsScope.Global ? "global-settings.json" : "project-settings.json",
            Title = scope == SettingsScope.Global
                ? L("SettingsHub.ExportTitle.Global", "Export Global Settings Bundle")
                : L("SettingsHub.ExportTitle.Project", "Export Project Settings Bundle"),
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
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

        MessageBoxResult confirmed = System.Windows.MessageBox.Show(
            message,
            ProjectInfo.Name,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.OK)
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
        int targetEpsg = int.TryParse(_epsgTextBox.Text, out int epsg) ? epsg : ProjectInfo.DefaultTargetEpsg;
        ExportDialogSettings settings = _snapshot.GlobalSettings;
        settings.OutputDirectory = (_outputDirectoryTextBox.Text ?? string.Empty).Trim();
        settings.PreviewBasemapUrlTemplate = (_basemapUrlTemplateTextBox.Text ?? string.Empty).Trim();
        settings.PreviewBasemapAttribution = (_basemapAttributionTextBox.Text ?? string.Empty).Trim();
        settings.TargetEpsg = targetEpsg;
        settings.GenerateDiagnosticsReport = _diagnosticsCheckBox.IsChecked == true;
        settings.GeneratePackageOutput = _packageCheckBox.IsChecked == true;
        settings.IncludePackageLegend = _packageLegendCheckBox.IsChecked == true;
        settings.UiLanguage = _language;
        settings.GeometryRepairOptions.Enabled = _repairEnabledCheckBox.IsChecked == true;
        return settings;
    }
    private ProjectMappingRules BuildProjectMappings()
    {
        return ProjectMappingRules.Create(
            ReadMappings(_floorRows),
            ReadMappings(_roomRows),
            ReadMappings(_familyRows),
            ReadFamilies(_openingRows));
    }

    private static Dictionary<string, string> ReadMappings(IEnumerable<MappingRow> rows)
    {
        Dictionary<string, string> mappings = new(StringComparer.Ordinal);
        foreach (MappingRow row in rows)
        {
            string key = (row.Key ?? string.Empty).Trim();
            string category = (row.Category ?? string.Empty).Trim();
            if (key.Length > 0 && category.Length > 0)
            {
                mappings[key] = category;
            }
        }

        return mappings;
    }

    private static List<string> ReadFamilies(IEnumerable<OpeningRow> rows)
    {
        return rows.Select(row => (row.FamilyName ?? string.Empty).Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ApplyLanguage()
    {
        _window.Title = L("SettingsHub.Title", "GeoExporter Settings");

        TabItem? globalTab = _tabs.Items.Count > 0 ? _tabs.Items[0] as TabItem : null;
        TabItem? projectTab = _tabs.Items.Count > 1 ? _tabs.Items[1] as TabItem : null;
        if (globalTab != null)
        {
            globalTab.Header = L("SettingsHub.GlobalTab", "Global");
            if (globalTab.Tag is GroupBox globalGroup)
            {
                globalGroup.Header = L("SettingsHub.DefaultsGroup", "Default Export Settings");
                if (globalGroup.Content is UIElement globalContent)
                {
                    UpdateLabelFactories(globalContent);
                }
            }
        }

        if (projectTab != null)
        {
            projectTab.Header = L("SettingsHub.ProjectTab", "Project");
            if (projectTab.Tag is ProjectTabRefs refs)
            {
                refs.Intro.Text = L("SettingsHub.ProjectMappingsIntro", "Manage exporter-side floor, room, family, and accepted opening mappings for the current project.");
                if (refs.MappingTabs.Items.Count > 0 && refs.MappingTabs.Items[0] is TabItem floorTab)
                {
                    floorTab.Header = L("SettingsHub.FloorOverridesTab", "Floor Overrides");
                }
                if (refs.MappingTabs.Items.Count > 1 && refs.MappingTabs.Items[1] is TabItem roomTab)
                {
                    roomTab.Header = "Room Categories";
                }
                if (refs.MappingTabs.Items.Count > 2 && refs.MappingTabs.Items[2] is TabItem familyTab)
                {
                    familyTab.Header = L("SettingsHub.FamilyCategoriesTab", "Family Categories");
                }
                if (refs.MappingTabs.Items.Count > 3 && refs.MappingTabs.Items[3] is TabItem openingTab)
                {
                    openingTab.Header = L("SettingsHub.AcceptedOpeningsTab", "Accepted Openings");
                }
            }
        }

        _statusGroup.Header = L("SettingsHub.ScopeStatusGroup", "Configuration Status");
        _saveButton.Content = L("Common.Save", "Save");
        _helpButton.Content = L("Common.Help", "Help");
        _closeButton.Content = L("Common.Close", "Close");
        _browseOutputDirectoryButton.Content = L("Common.Browse", "Browse...");
        int globalProfileCount = _snapshot.Profiles?.Count(profile => profile != null && profile.Scope == ExportProfileScope.Global) ?? 0;
        int projectProfileCount = _snapshot.Profiles?.Count(profile => profile != null && profile.Scope == ExportProfileScope.Project) ?? 0;
        _profileSummary.Text = string.Format(
            L("SettingsHub.ProfileSummary", "Global profiles: {0}    Project profiles: {1}"),
            globalProfileCount,
            projectProfileCount);

        if (_floorGrid.Columns.Count > 1)
        {
            _floorGrid.Columns[0].Header = L("SettingsHub.FloorTypeName", "Floor Type Name");
            _floorGrid.Columns[1].Header = L("SettingsHub.Category", "Category");
        }
        if (_roomGrid.Columns.Count > 1)
        {
            _roomGrid.Columns[0].Header = "Room Value";
            _roomGrid.Columns[1].Header = L("SettingsHub.Category", "Category");
        }
        if (_familyGrid.Columns.Count > 1)
        {
            _familyGrid.Columns[0].Header = L("SettingsHub.FamilyName", "Family Name");
            _familyGrid.Columns[1].Header = L("SettingsHub.Category", "Category");
        }
        if (_openingGrid.Columns.Count > 0)
        {
            _openingGrid.Columns[0].Header = L("SettingsHub.AcceptedOpeningFamilyName", "Accepted Opening Family Name");
        }

        _diagnosticsCheckBox.Content = L("ExportDialog.WriteDiagnostics", "Write diagnostics report");
        _packageCheckBox.Content = L("ExportDialog.WritePackage", "Write GIS package");
        _packageLegendCheckBox.Content = L("ExportDialog.IncludeLegend", "Include legend file");
        _repairEnabledCheckBox.Content = L("ExportDialog.GeometryRepair", "Geometry Repair");

        foreach (ScopeButtonSet set in _scopeButtons.Values)
        {
            set.ImportButton.Content = L("Common.Import", "Import...");
            set.ExportButton.Content = L("Common.Export", "Export...");
            set.ResetButton.Content = set.Scope == SettingsScope.Global
                ? L("Common.ResetDefaults", "Reset Defaults")
                : L("Common.ClearProjectData", "Clear Project Data");
        }

        _languageComboBox.Items.Refresh();
    }

    private static void UpdateLabelFactories(UIElement container)
    {
        if (container is TextBlock text && text.Tag is Func<string> factory)
        {
            text.Text = factory();
        }

        if (container is ContentControl contentControl && contentControl.Content is UIElement content)
        {
            UpdateLabelFactories(content);
        }

        if (container is Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                UpdateLabelFactories(child);
            }
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

    private static SettingsBundleSnapshot NormalizeSnapshot(SettingsBundleSnapshot snapshot)
    {
        snapshot ??= new SettingsBundleSnapshot();
        snapshot.GlobalSettings ??= new ExportDialogSettings();
        snapshot.GlobalSettings.GeometryRepairOptions ??= new RevitGeoExporter.Core.Geometry.GeometryRepairOptions();
        snapshot.GlobalSettings.PreviewBasemapUrlTemplate ??= PreviewBasemapSettings.DefaultUrlTemplate;
        snapshot.GlobalSettings.PreviewBasemapAttribution ??= PreviewBasemapSettings.DefaultAttribution;
        snapshot.Profiles = (snapshot.Profiles ?? Array.Empty<ExportProfile>())
            .Where(profile => profile != null)
            .ToList();
        snapshot.ProjectMappings ??= ProjectMappingRules.Empty;
        snapshot.StatusEntries ??= Array.Empty<SettingsStatusEntry>();
        return snapshot;
    }
    private string L(string key, string fallback) => LocalizedTextProvider.Get(_language, key, fallback);
    private sealed class LanguageItem
    {
        public LanguageItem(UiLanguage language)
        {
            Language = language;
        }

        public UiLanguage Language { get; }

        public override string ToString() => UiLanguageText.DisplayName(Language);
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

        public override string ToString() => $"EPSG:{Epsg} - {Name}";
    }

    private sealed class ScopeButtonSet
    {
        public ScopeButtonSet(Button importButton, Button exportButton, Button resetButton, StackPanel container, SettingsScope scope)
        {
            ImportButton = importButton;
            ExportButton = exportButton;
            ResetButton = resetButton;
            Container = container;
            Scope = scope;
        }

        public Button ImportButton { get; }

        public Button ExportButton { get; }

        public Button ResetButton { get; }

        public StackPanel Container { get; }

        public SettingsScope Scope { get; }
    }

    private sealed class ProjectTabRefs
    {
        public ProjectTabRefs(TextBlock intro, TabControl mappingTabs)
        {
            Intro = intro;
            MappingTabs = mappingTabs;
        }

        public TextBlock Intro { get; }

        public TabControl MappingTabs { get; }
    }

    private sealed class MappingRow
    {
        public string Key { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;
    }

    private sealed class OpeningRow
    {
        public string FamilyName { get; set; } = string.Empty;
    }
}




