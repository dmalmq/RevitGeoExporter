using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Export;
using WinForms = System.Windows.Forms;

namespace RevitGeoExporter.UI;

internal sealed class ExportDialogWpf : IDisposable
{
    private readonly ObservableCollection<ViewSelectionRow> _views = new();
    private readonly Action<ExportPreviewRequest>? _previewRequested;
    private readonly Window _window;
    private readonly List<ExportProfile> _profiles;

    private readonly ListBox _viewList = new();
    private readonly TextBox _outputDirectoryTextBox = new();
    private readonly TextBox _targetEpsgTextBox = new();
    private readonly ComboBox _languageComboBox = new();
    private readonly ComboBox _presetComboBox = new();
    private readonly ComboBox _unitSourceComboBox = new();
    private readonly TextBox _roomCategoryParameterTextBox = new();
    private readonly CheckBox _unitCheckBox = new();
    private readonly CheckBox _detailCheckBox = new();
    private readonly CheckBox _openingCheckBox = new();
    private readonly CheckBox _levelCheckBox = new();
    private readonly CheckBox _diagnosticsCheckBox = new();
    private readonly CheckBox _packageCheckBox = new();
    private readonly CheckBox _packageLegendCheckBox = new();
    private readonly TextBlock _experimentalLabel = new();

    public ExportDialogWpf(
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

        foreach (ViewPlan view in views)
        {
            _views.Add(new ViewSelectionRow(view));
        }

        _profiles = (profiles ?? Array.Empty<ExportProfile>()).ToList();
        _previewRequested = previewRequested;

        _window = new Window
        {
            Width = 900,
            Height = 700,
            MinWidth = 820,
            MinHeight = 620,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = BuildLayout(openMappingsRequested),
        };

        LoadValues(settings);
    }

    public ExportDialogResult? Result { get; private set; }

    public WinForms.DialogResult ShowDialog()
    {
        bool? result = _window.ShowDialog();
        return result == true ? WinForms.DialogResult.OK : WinForms.DialogResult.Cancel;
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
            SelectedViewIds = GetSelectedViews().Select(view => view.Id.Value).ToList(),
            GenerateDiagnosticsReport = _diagnosticsCheckBox.IsChecked == true,
            GeneratePackageOutput = _packageCheckBox.IsChecked == true,
            IncludePackageLegend = _packageLegendCheckBox.IsChecked == true,
            UiLanguage = ((_languageComboBox.SelectedItem as LanguageItem)?.Language) ?? UiLanguage.English,
            UnitSource = ((_unitSourceComboBox.SelectedItem as UnitSourceItem)?.Source) ?? UnitSource.Floors,
            RoomCategoryParameterName = (_roomCategoryParameterTextBox.Text ?? string.Empty).Trim(),
            GeometryRepairOptions = new GeometryRepairOptions(),
        };
    }

    public void Dispose()
    {
        if (_window.IsVisible)
        {
            _window.Close();
        }
    }

    private UIElement BuildLayout(Action? openMappingsRequested)
    {
        Grid root = new() { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid content = new();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.56, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.44, GridUnitType.Star) });

        GroupBox viewsGroup = new() { Header = "Plan Views", Margin = new Thickness(0, 0, 10, 0) };
        DockPanel viewPanel = new();
        _viewList.ItemsSource = _views;
        _viewList.DisplayMemberPath = nameof(ViewSelectionRow.DisplayText);
        viewPanel.Children.Add(_viewList);
        DockPanel.SetDock(BuildViewActions(), Dock.Bottom);
        viewPanel.Children.Add(BuildViewActions());
        viewsGroup.Content = viewPanel;
        content.Children.Add(viewsGroup);

        GroupBox optionsGroup = new() { Header = "Export Options" };
        optionsGroup.Content = BuildOptionsPanel(openMappingsRequested);
        Grid.SetColumn(optionsGroup, 1);
        content.Children.Add(optionsGroup);

        root.Children.Add(content);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };

        Button cancel = new() { Content = "Cancel", Width = 96 };
        cancel.Click += (_, _) => { _window.DialogResult = false; _window.Close(); };
        Button preview = new() { Content = "Preview...", Width = 110, Margin = new Thickness(8, 0, 0, 0) };
        preview.Click += (_, _) => ShowPreview();
        Button export = new() { Content = "Export", Width = 96, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        export.Click += (_, _) => ConfirmExport();

        actions.Children.Add(cancel);
        actions.Children.Add(preview);
        actions.Children.Add(export);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);

        return root;
    }

    private UIElement BuildViewActions()
    {
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        Button selectAll = new() { Content = "Select All", Width = 100 };
        selectAll.Click += (_, _) =>
        {
            foreach (ViewSelectionRow row in _views)
            {
                row.IsSelected = true;
            }
            _viewList.Items.Refresh();
        };

        Button clearAll = new() { Content = "Clear All", Width = 100, Margin = new Thickness(8, 0, 0, 0) };
        clearAll.Click += (_, _) =>
        {
            foreach (ViewSelectionRow row in _views)
            {
                row.IsSelected = false;
            }
            _viewList.Items.Refresh();
        };

        actions.Children.Add(selectAll);
        actions.Children.Add(clearAll);
        return actions;
    }

    private UIElement BuildOptionsPanel(Action? openMappingsRequested)
    {
        StackPanel panel = new() { Margin = new Thickness(10) };

        _experimentalLabel.Text = "WPF export dialog (phase 3 in progress). Profiles and advanced geometry repair controls remain in classic dialog.";
        _experimentalLabel.TextWrapping = TextWrapping.Wrap;
        _experimentalLabel.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(_experimentalLabel);

        panel.Children.Add(Labeled("Language", _languageComboBox));
        panel.Children.Add(Labeled("Feature Types", FeaturePanel()));
        panel.Children.Add(Labeled("Output Directory", _outputDirectoryTextBox));
        panel.Children.Add(Labeled("CRS Preset", _presetComboBox));
        panel.Children.Add(Labeled("Target EPSG", _targetEpsgTextBox));
        panel.Children.Add(Labeled("Unit Source", _unitSourceComboBox));
        panel.Children.Add(Labeled("Room Category Parameter", _roomCategoryParameterTextBox));

        _diagnosticsCheckBox.Content = "Write diagnostics report";
        _packageCheckBox.Content = "Write GIS package";
        _packageLegendCheckBox.Content = "Include legend file";
        panel.Children.Add(_diagnosticsCheckBox);
        panel.Children.Add(_packageCheckBox);
        panel.Children.Add(_packageLegendCheckBox);

        Button mappingsButton = new() { Content = "Mappings...", Width = 120, Margin = new Thickness(0, 8, 0, 0) };
        mappingsButton.Click += (_, _) => openMappingsRequested?.Invoke();
        panel.Children.Add(mappingsButton);

        _presetComboBox.SelectionChanged += (_, _) =>
        {
            if (_presetComboBox.SelectedItem is CrsPresetItem item)
            {
                _targetEpsgTextBox.Text = item.Epsg.ToString();
            }
        };

        _packageCheckBox.Checked += (_, _) => _packageLegendCheckBox.IsEnabled = true;
        _packageCheckBox.Unchecked += (_, _) => _packageLegendCheckBox.IsEnabled = false;

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel,
        };
    }

    private UIElement FeaturePanel()
    {
        StackPanel panel = new() { Orientation = Orientation.Vertical };
        _unitCheckBox.Content = "unit";
        _detailCheckBox.Content = "detail";
        _openingCheckBox.Content = "opening";
        _levelCheckBox.Content = "level";
        panel.Children.Add(_unitCheckBox);
        panel.Children.Add(_detailCheckBox);
        panel.Children.Add(_openingCheckBox);
        panel.Children.Add(_levelCheckBox);
        return panel;
    }

    private static UIElement Labeled(string label, UIElement control)
    {
        StackPanel panel = new() { Margin = new Thickness(0, 0, 0, 6) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(control);
        return panel;
    }

    private void LoadValues(ExportDialogSettings settings)
    {
        _window.Title = "Export GeoPackage";
        _outputDirectoryTextBox.Text = settings.OutputDirectory ?? string.Empty;
        _targetEpsgTextBox.Text = settings.TargetEpsg.ToString();

        _unitCheckBox.IsChecked = settings.FeatureTypes.HasFlag(ExportFeatureType.Unit);
        _detailCheckBox.IsChecked = settings.FeatureTypes.HasFlag(ExportFeatureType.Detail);
        _openingCheckBox.IsChecked = settings.FeatureTypes.HasFlag(ExportFeatureType.Opening);
        _levelCheckBox.IsChecked = settings.FeatureTypes.HasFlag(ExportFeatureType.Level);

        _diagnosticsCheckBox.IsChecked = settings.GenerateDiagnosticsReport;
        _packageCheckBox.IsChecked = settings.GeneratePackageOutput;
        _packageLegendCheckBox.IsChecked = settings.IncludePackageLegend;
        _packageLegendCheckBox.IsEnabled = _packageCheckBox.IsChecked == true;

        _languageComboBox.Items.Add(new LanguageItem(UiLanguage.English));
        _languageComboBox.Items.Add(new LanguageItem(UiLanguage.Japanese));
        _languageComboBox.SelectedIndex = settings.UiLanguage == UiLanguage.Japanese ? 1 : 0;

        foreach (KeyValuePair<int, string> zone in JapanPlaneRectangular.Zones.OrderBy(entry => entry.Key))
        {
            _presetComboBox.Items.Add(new CrsPresetItem(zone.Key, zone.Value));
            if (zone.Key == settings.TargetEpsg)
            {
                _presetComboBox.SelectedItem = _presetComboBox.Items[_presetComboBox.Items.Count - 1];
            }
        }

        _unitSourceComboBox.Items.Add(new UnitSourceItem(UnitSource.Floors));
        _unitSourceComboBox.Items.Add(new UnitSourceItem(UnitSource.Rooms));
        _unitSourceComboBox.SelectedIndex = settings.UnitSource == UnitSource.Rooms ? 1 : 0;
        _roomCategoryParameterTextBox.Text = settings.RoomCategoryParameterName ?? "Name";

        HashSet<long> selectedIds = new(settings.SelectedViewIds ?? new List<long>());
        foreach (ViewSelectionRow row in _views)
        {
            row.IsSelected = selectedIds.Contains(row.View.Id.Value);
        }
        _viewList.Items.Refresh();
    }

    private void ConfirmExport()
    {
        List<ViewPlan> selectedViews = GetSelectedViews();
        if (selectedViews.Count == 0)
        {
            MessageBox.Show("Select at least one view.", _window.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ExportFeatureType featureTypes = GetSelectedFeatureTypes();
        if (featureTypes == ExportFeatureType.None)
        {
            MessageBox.Show("Select at least one feature type.", _window.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(_targetEpsgTextBox.Text, out int epsg) || epsg <= 0)
        {
            MessageBox.Show("Enter a valid EPSG code.", _window.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UiLanguage uiLanguage = (_languageComboBox.SelectedItem as LanguageItem)?.Language ?? UiLanguage.English;
        UnitSource unitSource = (_unitSourceComboBox.SelectedItem as UnitSourceItem)?.Source ?? UnitSource.Floors;

        Result = new ExportDialogResult(
            selectedViews,
            (_outputDirectoryTextBox.Text ?? string.Empty).Trim(),
            epsg,
            featureTypes,
            _diagnosticsCheckBox.IsChecked == true,
            _packageCheckBox.IsChecked == true,
            _packageLegendCheckBox.IsChecked == true,
            new GeometryRepairOptions(),
            selectedProfileName: _profiles.FirstOrDefault()?.Name,
            uiLanguage,
            unitSource,
            (_roomCategoryParameterTextBox.Text ?? string.Empty).Trim());

        _window.DialogResult = true;
        _window.Close();
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
            MessageBox.Show("Select at least one view to preview.", _window.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _previewRequested(new ExportPreviewRequest(
            selectedViews,
            GetSelectedFeatureTypes(),
            new GeometryRepairOptions(),
            (_languageComboBox.SelectedItem as LanguageItem)?.Language ?? UiLanguage.English,
            (_unitSourceComboBox.SelectedItem as UnitSourceItem)?.Source ?? UnitSource.Floors,
            (_roomCategoryParameterTextBox.Text ?? string.Empty).Trim()));
    }

    private List<ViewPlan> GetSelectedViews() => _views.Where(x => x.IsSelected).Select(x => x.View).ToList();

    private ExportFeatureType GetSelectedFeatureTypes()
    {
        ExportFeatureType types = ExportFeatureType.None;
        if (_unitCheckBox.IsChecked == true) types |= ExportFeatureType.Unit;
        if (_detailCheckBox.IsChecked == true) types |= ExportFeatureType.Detail;
        if (_openingCheckBox.IsChecked == true) types |= ExportFeatureType.Opening;
        if (_levelCheckBox.IsChecked == true) types |= ExportFeatureType.Level;
        return types;
    }

    private sealed class ViewSelectionRow
    {
        public ViewSelectionRow(ViewPlan view)
        {
            View = view;
            DisplayText = $"{view.Name} [Level: {view.GenLevel?.Name ?? "(none)"}]";
        }

        public ViewPlan View { get; }

        public string DisplayText { get; }

        public bool IsSelected { get; set; }

        public override string ToString() => DisplayText;
    }

    private sealed class LanguageItem
    {
        public LanguageItem(UiLanguage language) => Language = language;

        public UiLanguage Language { get; }

        public override string ToString() => UiLanguageText.DisplayName(Language);
    }

    private sealed class UnitSourceItem
    {
        public UnitSourceItem(UnitSource source) => Source = source;

        public UnitSource Source { get; }

        public override string ToString() => Source == UnitSource.Rooms ? "Rooms" : "Floors";
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
}
