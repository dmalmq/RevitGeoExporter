using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Revit.DB;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Export;
using RevitGeoExporter.Resources;
using WinForms = System.Windows.Forms;
using WpfGrid = System.Windows.Controls.Grid;

namespace RevitGeoExporter.UI;

internal sealed class ExportDialogWpf : IDisposable
{
    private readonly ObservableCollection<ViewSelectionRow> _views = new();
    private readonly Action<ExportPreviewRequest, WinForms.IWin32Window?>? _previewRequested;
    private readonly Window _window;
    private readonly List<ExportProfile> _profiles;
    private readonly ModelCoordinateInfo? _coordinateInfo;
    private readonly PreviewBasemapSettings _previewBasemapSettings;

    private UiLanguage _language = UiLanguage.English;
    private readonly ListBox _viewList = new();
    private readonly TextBox _outputDirectoryTextBox = new();
    private readonly TextBox _targetEpsgTextBox = new();
    private readonly ComboBox _languageComboBox = new();
    private readonly ComboBox _presetComboBox = new();
    private readonly ComboBox _coordinateModeComboBox = new();
    private readonly ComboBox _unitSourceComboBox = new();
    private readonly TextBox _roomCategoryParameterTextBox = new();
    private readonly CheckBox _unitCheckBox = new();
    private readonly CheckBox _detailCheckBox = new();
    private readonly CheckBox _openingCheckBox = new();
    private readonly CheckBox _levelCheckBox = new();
    private readonly CheckBox _diagnosticsCheckBox = new();
    private readonly CheckBox _packageCheckBox = new();
    private readonly CheckBox _packageLegendCheckBox = new();
    private readonly GroupBox _viewsGroup = new();
    private readonly GroupBox _optionsGroup = new();
    private readonly Button _cancelButton = new();
    private readonly Button _previewButton = new();
    private readonly Button _exportButton = new();
    private readonly Button _selectAllButton = new();
    private readonly Button _clearAllButton = new();
    private readonly Button _mappingsButton = new();
    private readonly TextBlock _languageLabel = new();
    private readonly TextBlock _featureTypesLabel = new();
    private readonly TextBlock _outputDirectoryLabel = new();
    private readonly TextBlock _crsPresetLabel = new();
    private readonly TextBlock _targetEpsgLabel = new();
    private readonly TextBlock _coordinateModeLabel = new();
    private readonly TextBlock _unitSourceLabel = new();
    private readonly TextBlock _roomCategoryParameterLabel = new();
    private readonly TextBlock _coordinateInfoTitle = new();
    private readonly TextBlock _displayUnitsInfoText = new();
    private readonly TextBlock _projectLocationInfoText = new();
    private readonly TextBlock _siteCrsInfoText = new();
    private readonly TextBlock _sharedCoordinateInfoText = new();
    private readonly TextBlock _modeSummaryText = new();

    public ExportDialogWpf(
        IReadOnlyList<ViewPlan> views,
        ExportDialogSettings settings,
        IReadOnlyList<ExportProfile>? profiles = null,
        Action<ExportProfileScope, string, ExportDialogSettings>? saveProfileRequested = null,
        Action<ExportProfile, string>? renameProfileRequested = null,
        Action<ExportProfile>? deleteProfileRequested = null,
        Action? openMappingsRequested = null,
        Action<ExportPreviewRequest, WinForms.IWin32Window?>? previewRequested = null,
        ModelCoordinateInfo? coordinateInfo = null)
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
        _coordinateInfo = coordinateInfo;
        _previewBasemapSettings = new PreviewBasemapSettings(settings.PreviewBasemapUrlTemplate, settings.PreviewBasemapAttribution);

        _window = new Window
        {
            Width = 940,
            Height = 760,
            MinWidth = 840,
            MinHeight = 660,
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
        int targetEpsg = ParseTargetEpsgOrDefault();

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
            CoordinateMode = GetSelectedCoordinateMode(),
            UnitSource = ((_unitSourceComboBox.SelectedItem as UnitSourceItem)?.Source) ?? UnitSource.Floors,
            RoomCategoryParameterName = (_roomCategoryParameterTextBox.Text ?? string.Empty).Trim(),
            PreviewBasemapUrlTemplate = _previewBasemapSettings.UrlTemplate,
            PreviewBasemapAttribution = _previewBasemapSettings.Attribution,
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
        WpfGrid root = new() { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        WpfGrid content = new();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.56, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.44, GridUnitType.Star) });

        _viewsGroup.Margin = new Thickness(0, 0, 10, 0);
        DockPanel viewPanel = new() { LastChildFill = true };
        _viewList.ItemsSource = _views;
        _viewList.ItemTemplate = BuildViewSelectionTemplate();
        _viewList.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnPreviewInputsChanged));
        _viewList.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnPreviewInputsChanged));
        UIElement viewActions = BuildViewActions();
        DockPanel.SetDock(viewActions, Dock.Bottom);
        viewPanel.Children.Add(viewActions);
        viewPanel.Children.Add(_viewList);
        _viewsGroup.Content = viewPanel;
        content.Children.Add(_viewsGroup);

        _optionsGroup.Content = BuildOptionsPanel(openMappingsRequested);
        WpfGrid.SetColumn(_optionsGroup, 1);
        content.Children.Add(_optionsGroup);

        root.Children.Add(content);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };

        _cancelButton.Width = 96;
        _cancelButton.Click += (_, _) => { _window.DialogResult = false; _window.Close(); };
        _previewButton.Width = 110;
        _previewButton.Margin = new Thickness(8, 0, 0, 0);
        _previewButton.Click += (_, _) => ShowPreview();
        _exportButton.Width = 96;
        _exportButton.Margin = new Thickness(8, 0, 0, 0);
        _exportButton.IsDefault = true;
        _exportButton.Click += (_, _) => ConfirmExport();

        actions.Children.Add(_cancelButton);
        actions.Children.Add(_previewButton);
        actions.Children.Add(_exportButton);
        WpfGrid.SetRow(actions, 1);
        root.Children.Add(actions);

        return root;
    }

    private DataTemplate BuildViewSelectionTemplate()
    {
        FrameworkElementFactory checkBox = new(typeof(CheckBox));
        checkBox.SetBinding(CheckBox.ContentProperty, new System.Windows.Data.Binding(nameof(ViewSelectionRow.DisplayText)));
        checkBox.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding(nameof(ViewSelectionRow.IsSelected))
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged,
        });
        checkBox.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 1, 2, 1));

        return new DataTemplate
        {
            VisualTree = checkBox,
        };
    }

    private UIElement BuildViewActions()
    {
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        _selectAllButton.Width = 100;
        _selectAllButton.Click += (_, _) =>
        {
            foreach (ViewSelectionRow row in _views)
            {
                row.IsSelected = true;
            }
            _viewList.Items.Refresh();
            UpdatePreviewButtonEnabled();
        };

        _clearAllButton.Width = 100;
        _clearAllButton.Margin = new Thickness(8, 0, 0, 0);
        _clearAllButton.Click += (_, _) =>
        {
            foreach (ViewSelectionRow row in _views)
            {
                row.IsSelected = false;
            }
            _viewList.Items.Refresh();
            UpdatePreviewButtonEnabled();
        };

        actions.Children.Add(_selectAllButton);
        actions.Children.Add(_clearAllButton);
        return actions;
    }

    private UIElement BuildOptionsPanel(Action? openMappingsRequested)
    {
        StackPanel panel = new() { Margin = new Thickness(10) };

        _languageComboBox.SelectionChanged += (_, _) =>
        {
            if (_languageComboBox.SelectedItem is LanguageItem item)
            {
                _language = item.Language;
                ViewSelectionRow.DisplayLanguage = _language;
                CoordinateModeItem.DisplayLanguage = _language;
                _viewList.Items.Refresh();
                _unitSourceComboBox.Items.Refresh();
                _presetComboBox.Items.Refresh();
                _languageComboBox.Items.Refresh();
                _coordinateModeComboBox.Items.Refresh();
                ApplyLanguage();
                UpdateCoordinateModeUi();
            }
        };

        _coordinateModeComboBox.SelectionChanged += (_, _) => UpdateCoordinateModeUi();
        _targetEpsgTextBox.TextChanged += (_, _) => UpdateCoordinateModeUi();

        panel.Children.Add(Labeled(_languageLabel, _languageComboBox));
        panel.Children.Add(Labeled(_featureTypesLabel, FeaturePanel()));
        panel.Children.Add(Labeled(_outputDirectoryLabel, _outputDirectoryTextBox));
        panel.Children.Add(Labeled(_coordinateModeLabel, _coordinateModeComboBox));
        panel.Children.Add(Labeled(_crsPresetLabel, _presetComboBox));
        panel.Children.Add(Labeled(_targetEpsgLabel, _targetEpsgTextBox));
        panel.Children.Add(BuildCoordinateInfoPanel());
        panel.Children.Add(Labeled(_unitSourceLabel, _unitSourceComboBox));
        panel.Children.Add(Labeled(_roomCategoryParameterLabel, _roomCategoryParameterTextBox));

        panel.Children.Add(_diagnosticsCheckBox);
        panel.Children.Add(_packageCheckBox);
        panel.Children.Add(_packageLegendCheckBox);

        _mappingsButton.Width = 140;
        _mappingsButton.Margin = new Thickness(0, 8, 0, 0);
        _mappingsButton.Click += (_, _) => openMappingsRequested?.Invoke();
        panel.Children.Add(_mappingsButton);

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

    private UIElement BuildCoordinateInfoPanel()
    {
        Border border = new()
        {
            BorderBrush = Brushes.Gainsboro,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8),
            Background = Brushes.WhiteSmoke,
        };

        StackPanel panel = new();
        _coordinateInfoTitle.FontWeight = FontWeights.SemiBold;
        _coordinateInfoTitle.Margin = new Thickness(0, 0, 0, 4);
        panel.Children.Add(_coordinateInfoTitle);

        foreach (TextBlock block in new[]
                 {
                     _displayUnitsInfoText,
                     _projectLocationInfoText,
                     _siteCrsInfoText,
                     _sharedCoordinateInfoText,
                     _modeSummaryText,
                 })
        {
            block.TextWrapping = TextWrapping.Wrap;
            block.Margin = new Thickness(0, 0, 0, 2);
            panel.Children.Add(block);
        }

        border.Child = panel;
        return border;
    }

    private UIElement FeaturePanel()
    {
        StackPanel panel = new() { Orientation = Orientation.Vertical };
        _unitCheckBox.Content = "unit";
        _detailCheckBox.Content = "detail";
        _openingCheckBox.Content = "opening";
        _levelCheckBox.Content = "level";
        _unitCheckBox.Checked += OnPreviewInputsChanged;
        _unitCheckBox.Unchecked += OnPreviewInputsChanged;
        _detailCheckBox.Checked += OnPreviewInputsChanged;
        _detailCheckBox.Unchecked += OnPreviewInputsChanged;
        _openingCheckBox.Checked += OnPreviewInputsChanged;
        _openingCheckBox.Unchecked += OnPreviewInputsChanged;
        _levelCheckBox.Checked += OnPreviewInputsChanged;
        _levelCheckBox.Unchecked += OnPreviewInputsChanged;
        panel.Children.Add(_unitCheckBox);
        panel.Children.Add(_detailCheckBox);
        panel.Children.Add(_openingCheckBox);
        panel.Children.Add(_levelCheckBox);
        return panel;
    }

    private static UIElement Labeled(TextBlock label, UIElement control)
    {
        StackPanel panel = new() { Margin = new Thickness(0, 0, 0, 6) };
        label.Margin = new Thickness(0, 0, 0, 2);
        panel.Children.Add(label);
        panel.Children.Add(control);
        return panel;
    }

    private void LoadValues(ExportDialogSettings settings)
    {
        _language = Enum.IsDefined(typeof(UiLanguage), settings.UiLanguage)
            ? settings.UiLanguage
            : UiLanguage.English;
        ViewSelectionRow.DisplayLanguage = _language;
        CoordinateModeItem.DisplayLanguage = _language;

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
        _languageComboBox.SelectedIndex = _language == UiLanguage.Japanese ? 1 : 0;

        _coordinateModeComboBox.Items.Add(new CoordinateModeItem(CoordinateExportMode.SharedCoordinates));
        _coordinateModeComboBox.Items.Add(new CoordinateModeItem(CoordinateExportMode.ConvertToTargetCrs));
        _coordinateModeComboBox.SelectedIndex = settings.CoordinateMode == CoordinateExportMode.ConvertToTargetCrs ? 1 : 0;

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
        _unitSourceComboBox.Items.Refresh();
        _presetComboBox.Items.Refresh();
        _languageComboBox.Items.Refresh();
        _coordinateModeComboBox.Items.Refresh();
        ApplyLanguage();
        UpdateCoordinateModeUi();
        UpdatePreviewButtonEnabled();
    }

    private void ConfirmExport()
    {
        List<ViewPlan> selectedViews = GetSelectedViews();
        if (selectedViews.Count == 0)
        {
            MessageBox.Show(
                _window,
                L("ExportDialog.Message.SelectPlanViewToExport", "Select at least one plan view to export."),
                _window.Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ExportFeatureType featureTypes = GetSelectedFeatureTypes();
        if (featureTypes == ExportFeatureType.None)
        {
            MessageBox.Show(
                _window,
                L("ExportDialog.Message.SelectFeatureType", "Select at least one feature type."),
                _window.Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        CoordinateExportMode coordinateMode = GetSelectedCoordinateMode();
        if (coordinateMode == CoordinateExportMode.ConvertToTargetCrs)
        {
            if (!int.TryParse(_targetEpsgTextBox.Text, out int convertEpsg) || convertEpsg <= 0)
            {
                MessageBox.Show(
                    _window,
                    L("ExportDialog.Message.EnterValidEpsg", "Enter a valid EPSG code."),
                    _window.Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_coordinateInfo?.CanConvert != true)
            {
                MessageBox.Show(
                    _window,
                    L("ExportDialog.Message.SharedCrsRequiredForConversion", "Conversion requires a recognizable shared/site coordinate system in the current Revit model."),
                    _window.Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        UiLanguage uiLanguage = (_languageComboBox.SelectedItem as LanguageItem)?.Language ?? UiLanguage.English;
        UnitSource unitSource = (_unitSourceComboBox.SelectedItem as UnitSourceItem)?.Source ?? UnitSource.Floors;

        Result = new ExportDialogResult(
            selectedViews,
            (_outputDirectoryTextBox.Text ?? string.Empty).Trim(),
            ParseTargetEpsgOrDefault(),
            featureTypes,
            _diagnosticsCheckBox.IsChecked == true,
            _packageCheckBox.IsChecked == true,
            _packageLegendCheckBox.IsChecked == true,
            new GeometryRepairOptions(),
            selectedProfileName: _profiles.FirstOrDefault()?.Name,
            uiLanguage,
            coordinateMode,
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
            MessageBox.Show(
                _window,
                L("ExportDialog.Message.SelectPlanViewToPreview", "Select at least one plan view to preview."),
                _window.Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (GetSelectedFeatureTypes() == ExportFeatureType.None)
        {
            MessageBox.Show(
                _window,
                L("ExportDialog.Message.PreviewRequiresFeatureType", "Preview requires at least one selected feature type."),
                _window.Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ExportPreviewRequest previewRequest = new(
            selectedViews,
            GetSelectedFeatureTypes(),
            new GeometryRepairOptions(),
            (_languageComboBox.SelectedItem as LanguageItem)?.Language ?? UiLanguage.English,
            GetSelectedCoordinateMode(),
            ParseTargetEpsgOrDefault(),
            _coordinateInfo?.ResolvedSourceEpsg,
            _coordinateInfo?.SiteCoordinateSystemId,
            _coordinateInfo?.SiteCoordinateSystemDefinition,
            _coordinateInfo?.SurveyPointSharedCoordinates,
            (_unitSourceComboBox.SelectedItem as UnitSourceItem)?.Source ?? UnitSource.Floors,
            (_roomCategoryParameterTextBox.Text ?? string.Empty).Trim(),
            _previewBasemapSettings.UrlTemplate,
            _previewBasemapSettings.Attribution);

        IntPtr handle = new WindowInteropHelper(_window).EnsureHandle();
        WinForms.IWin32Window? owner = handle == IntPtr.Zero
            ? null
            : new Win32WindowOwner(handle);

        try
        {
            _previewRequested(previewRequest, owner);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _window,
                $"{L("ExportDialog.Message.PreviewOpenFailed", "Preview could not be opened.")}\n\n{ex.Message}",
                _window.Title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        _window.Activate();
    }

    private void UpdatePreviewButtonEnabled()
    {
        _previewButton.IsEnabled = _previewRequested != null &&
                                   GetSelectedFeatureTypes() != ExportFeatureType.None &&
                                   GetSelectedViews().Count > 0;
    }

    private void OnPreviewInputsChanged(object? sender, RoutedEventArgs e)
    {
        UpdatePreviewButtonEnabled();
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

    private CoordinateExportMode GetSelectedCoordinateMode()
    {
        return (_coordinateModeComboBox.SelectedItem as CoordinateModeItem)?.Mode ?? CoordinateExportMode.SharedCoordinates;
    }

    private int ParseTargetEpsgOrDefault()
    {
        if (int.TryParse(_targetEpsgTextBox.Text, out int epsg) && epsg > 0)
        {
            return epsg;
        }

        return _coordinateInfo?.ResolvedSourceEpsg ?? ProjectInfo.DefaultTargetEpsg;
    }

    private void UpdateCoordinateModeUi()
    {
        bool isConvertMode = GetSelectedCoordinateMode() == CoordinateExportMode.ConvertToTargetCrs;
        _presetComboBox.IsEnabled = isConvertMode;
        _targetEpsgTextBox.IsEnabled = isConvertMode;
        UpdateCoordinateInfoText(isConvertMode);
    }

    private void UpdateCoordinateInfoText(bool isConvertMode)
    {
        string displayUnits = _coordinateInfo?.DisplayLengthUnitLabel ?? "Unknown";
        string locationName = string.IsNullOrWhiteSpace(_coordinateInfo?.ActiveProjectLocationName)
            ? "Unavailable"
            : _coordinateInfo!.ActiveProjectLocationName;
        string sharedCrs = !string.IsNullOrWhiteSpace(_coordinateInfo?.ResolvedSourceLabel)
            ? _coordinateInfo!.ResolvedSourceLabel
            : "Not resolved";
        string siteId = !string.IsNullOrWhiteSpace(_coordinateInfo?.SiteCoordinateSystemId)
            ? _coordinateInfo!.SiteCoordinateSystemId
            : "Not set";
        string sharedSummary = !string.IsNullOrWhiteSpace(_coordinateInfo?.SharedCoordinateSummary)
            ? _coordinateInfo!.SharedCoordinateSummary
            : "Unavailable";

        _displayUnitsInfoText.Text = $"{L("ExportDialog.ModelUnits", "Model display units")}: {displayUnits}";
        _projectLocationInfoText.Text = $"{L("ExportDialog.ActiveLocation", "Active project location")}: {locationName}";
        _siteCrsInfoText.Text = $"{L("ExportDialog.SharedCrs", "Shared CRS")}: {sharedCrs}    ({L("ExportDialog.SiteCrsId", "Site CRS Id")}: {siteId})";
        _sharedCoordinateInfoText.Text = $"{L("ExportDialog.SharedCoordinateSummary", "Shared origin / transform")}: {sharedSummary}";

        if (isConvertMode)
        {
            if (_coordinateInfo?.CanConvert == true)
            {
                _modeSummaryText.Text = $"{L("ExportDialog.ModeSummary.Convert", "Export will convert from the model shared CRS to the selected target EPSG.")} ({sharedCrs} -> EPSG:{ParseTargetEpsgOrDefault()})";
                _modeSummaryText.Foreground = Brushes.DarkSlateGray;
            }
            else
            {
                _modeSummaryText.Text = L("ExportDialog.ModeSummary.ConvertUnavailable", "Conversion is unavailable until the Revit model has a recognizable shared/site CRS.");
                _modeSummaryText.Foreground = Brushes.DarkRed;
            }
        }
        else
        {
            string sharedOutput = _coordinateInfo?.ResolvedSourceEpsg is int sourceEpsg
                ? JapanPlaneRectangular.DescribeEpsg(sourceEpsg)
                : $"EPSG:{ParseTargetEpsgOrDefault()}";
            _modeSummaryText.Text = $"{L("ExportDialog.ModeSummary.Shared", "Export will stay in the model shared coordinate system by default.")} ({sharedOutput})";
            _modeSummaryText.Foreground = Brushes.DarkSlateGray;
        }
    }

    private void ApplyLanguage()
    {
        _window.Title = L("ExportDialog.Title", "Export GeoPackage");
        _viewsGroup.Header = L("ExportDialog.PlanViews", "Plan Views");
        _optionsGroup.Header = L("ExportDialog.Options", "Export Options");
        _languageLabel.Text = L("Common.Language", "Language");
        _featureTypesLabel.Text = L("ExportDialog.FeatureTypes", "Feature Types");
        _outputDirectoryLabel.Text = L("Common.OutputDirectory", "Output Directory");
        _coordinateModeLabel.Text = L("ExportDialog.CoordinateMode", "Coordinate Mode");
        _crsPresetLabel.Text = L("Common.CrsPreset", "CRS Preset");
        _targetEpsgLabel.Text = L("Common.TargetEpsg", "Target EPSG");
        _coordinateInfoTitle.Text = L("ExportDialog.ModelCoordinateInfo", "Model Coordinate Info");
        _unitSourceLabel.Text = "Unit Source";
        _roomCategoryParameterLabel.Text = "Room Category Parameter";
        _unitCheckBox.Content = "unit";
        _detailCheckBox.Content = "detail";
        _openingCheckBox.Content = "opening";
        _levelCheckBox.Content = "level";
        _diagnosticsCheckBox.Content = L("ExportDialog.WriteDiagnostics", "Write diagnostics report");
        _packageCheckBox.Content = L("ExportDialog.WritePackage", "Write GIS package");
        _packageLegendCheckBox.Content = L("ExportDialog.IncludeLegend", "Include legend file");
        _mappingsButton.Content = $"{LocalizedTextProvider.Get(_language, "Common.ProjectMappings", "Project Mappings")}...";
        _selectAllButton.Content = L("ExportDialog.SelectAll", "Select All");
        _clearAllButton.Content = L("ExportDialog.ClearAll", "Clear All");
        _cancelButton.Content = L("Common.Cancel", "Cancel");
        _previewButton.Content = L("ExportDialog.Preview", "Preview...");
        _exportButton.Content = L("ExportDialog.ExportButton", "Export");
        UpdateCoordinateInfoText(GetSelectedCoordinateMode() == CoordinateExportMode.ConvertToTargetCrs);
    }

    private string L(string key, string fallback) => LocalizedTextProvider.Get(_language, key, fallback);

    private sealed class Win32WindowOwner : WinForms.IWin32Window
    {
        public Win32WindowOwner(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }
    }

    private sealed class ViewSelectionRow
    {
        public static UiLanguage DisplayLanguage { get; set; } = UiLanguage.English;

        public ViewSelectionRow(ViewPlan view)
        {
            View = view;
        }

        public ViewPlan View { get; }

        public string DisplayText
        {
            get
            {
                string levelName = View.GenLevel?.Name ?? LocalizedTextProvider.Get(DisplayLanguage, "Common.NoLevel", "<no level>");
                string levelLabel = LocalizedTextProvider.Get(DisplayLanguage, "Common.Level", "Level");
                return $"{View.Name} [{levelLabel}: {levelName}]";
            }
        }

        public bool IsSelected { get; set; }

        public override string ToString() => DisplayText;
    }

    private sealed class LanguageItem
    {
        public LanguageItem(UiLanguage language) => Language = language;

        public UiLanguage Language { get; }

        public override string ToString() => UiLanguageText.DisplayName(Language);
    }

    private sealed class CoordinateModeItem
    {
        public static UiLanguage DisplayLanguage { get; set; } = UiLanguage.English;

        public CoordinateModeItem(CoordinateExportMode mode)
        {
            Mode = mode;
        }

        public CoordinateExportMode Mode { get; }

        public override string ToString()
        {
            return Mode == CoordinateExportMode.ConvertToTargetCrs
                ? LocalizedTextProvider.Get(DisplayLanguage, "ExportDialog.CoordinateMode.Convert", "Convert to target CRS")
                : LocalizedTextProvider.Get(DisplayLanguage, "ExportDialog.CoordinateMode.Shared", "Shared coordinates (default)");
        }
    }

    private sealed class UnitSourceItem
    {
        public UnitSourceItem(UnitSource source) => Source = source;

        public UnitSource Source { get; }

        public override string ToString() => Source == UnitSource.Rooms
            ? "Rooms"
            : "Floors";
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





