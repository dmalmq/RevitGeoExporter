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
using RevitGeoExporter.Help;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Core.Schema;
using RevitGeoExporter.Core.Validation;
using RevitGeoExporter.Export;
using WinForms = System.Windows.Forms;
using WpfGrid = System.Windows.Controls.Grid;

namespace RevitGeoExporter.UI;

 internal sealed class ExportDialogWpf : IDisposable
 {
    private static readonly Brush WindowBackgroundBrush = WpfDialogChrome.WindowBackgroundBrush;
    private static readonly Brush CardBorderBrush = WpfDialogChrome.CardBorderBrush;
    private static readonly Brush MutedTextBrush = WpfDialogChrome.MutedTextBrush;
    private static readonly Brush StatusBackgroundBrush = WpfDialogChrome.StatusBackgroundBrush;
    private static readonly Brush StatusWarningBackgroundBrush = WpfDialogChrome.StatusWarningBackgroundBrush;
    private static readonly Brush StatusWarningBorderBrush = WpfDialogChrome.StatusWarningBorderBrush;
    private static readonly Brush StatusTextBrush = WpfDialogChrome.StatusTextBrush;
    private static readonly Brush StatusWarningTextBrush = WpfDialogChrome.StatusWarningTextBrush;

    private readonly ObservableCollection<ViewSelectionRow> _views = new();
    private readonly ObservableCollection<LinkSelectionRow> _links = new();
    private readonly Action<ExportPreviewRequest, WinForms.IWin32Window?>? _previewRequested;
    private readonly Action? _openMappingsRequested;
    private readonly Window _window;
    private readonly List<ExportProfile> _profiles;
    private readonly ModelCoordinateInfo? _coordinateInfo;
    private readonly PreviewBasemapSettings _previewBasemapSettings;

    private UiLanguage _language = UiLanguage.English;
    private bool _isInitializing;
    private readonly ListBox _viewList = new();
    private readonly TextBox _outputDirectoryTextBox = new();
    private readonly TextBox _targetEpsgTextBox = new();
    private readonly ComboBox _languageComboBox = new();
    private readonly ComboBox _presetComboBox = new();
    private readonly ComboBox _coordinateModeComboBox = new();
    private readonly ComboBox _unitSourceComboBox = new();
    private readonly ComboBox _unitAttributeSourceComboBox = new();
    private readonly ComboBox _schemaProfileComboBox = new();
    private readonly ComboBox _validationPolicyComboBox = new();
    private readonly TextBox _roomCategoryParameterTextBox = new();
    private readonly CheckBox _unitCheckBox = new();
    private readonly CheckBox _detailCheckBox = new();
    private readonly CheckBox _openingCheckBox = new();
    private readonly CheckBox _levelCheckBox = new();
    private readonly CheckBox _diagnosticsCheckBox = new();
    private readonly CheckBox _packageCheckBox = new();
    private readonly CheckBox _packageLegendCheckBox = new();
    private readonly CheckBox _includeLinkedModelsCheckBox = new();
    private readonly Button _browseButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _previewButton = new();
    private readonly Button _exportButton = new();
    private readonly Button _selectAllButton = new();
    private readonly Button _clearAllButton = new();
    private readonly Button _helpButton = new();
    private readonly Button _mappingsButton = new();
    private readonly Button _manageSchemaProfilesButton = new();
    private readonly Button _manageValidationPoliciesButton = new();
    private readonly TextBlock _viewsTitleText = new();
    private readonly TextBlock _viewSelectionSummaryText = new();
    private readonly TextBlock _exportToTitleText = new();
    private readonly TextBlock _exportToDescriptionText = new();
    private readonly TextBlock _includeTitleText = new();
    private readonly TextBlock _includeDescriptionText = new();
    private readonly TextBlock _coordinateTitleText = new();
    private readonly TextBlock _coordinateDescriptionText = new();
    private readonly TextBlock _advancedTitleText = new();
    private readonly TextBlock _advancedDescriptionText = new();
    private readonly TextBlock _outputDirectoryLabel = new();
    private readonly TextBlock _coordinateStatusTitleText = new();
    private readonly TextBlock _coordinateStatusText = new();
    private readonly TextBlock _coordinateStatusDetailText = new();
    private readonly TextBlock _crsPresetLabel = new();
    private readonly TextBlock _targetEpsgLabel = new();
    private readonly TextBlock _coordinateModeLabel = new();
    private readonly TextBlock _coordinateSettingsHeaderText = new();
    private readonly TextBlock _technicalDetailsHeaderText = new();
    private readonly TextBlock _unitSourceLabel = new();
    private readonly TextBlock _unitAttributeSourceLabel = new();
    private readonly TextBlock _roomCategoryParameterLabel = new();
    private readonly TextBlock _schemaProfileLabel = new();
    private readonly TextBlock _validationPolicyLabel = new();
    private readonly TextBlock _linkedModelsLabel = new();
    private readonly TextBlock _displayUnitsInfoText = new();
    private readonly TextBlock _projectLocationInfoText = new();
    private readonly TextBlock _siteCrsInfoText = new();
    private readonly TextBlock _sharedCoordinateInfoText = new();
    private readonly TextBlock _advancedOptionsHeaderText = new();
    private readonly TextBlock _footerSummaryText = new();
    private readonly TextBlock _versionText = new();
    private readonly TextBlock _languageLabel = new();
    private readonly Border _coordinateStatusCard = new();
    private readonly Expander _coordinateSettingsExpander = new();
    private readonly Expander _technicalDetailsExpander = new();
    private readonly Expander _advancedOptionsExpander = new();
    private readonly StackPanel _convertSettingsPanel = new();
    private readonly ListBox _linkList = new();
    private FrameworkElement? _unitSourceRow;
    private FrameworkElement? _unitAttributeSourceRow;
    private FrameworkElement? _roomCategoryParameterRow;
    private FrameworkElement? _schemaProfileRow;
    private FrameworkElement? _validationPolicyRow;
    private FrameworkElement? _linkedModelsRow;
    private FrameworkElement? _legendOptionRow;
    private UnitSource _unitSource = UnitSource.Floors;
    private LinkExportOptions _linkExportOptions = new();
    private List<SchemaProfile> _schemaProfiles = new() { SchemaProfile.CreateCoreProfile() };
    private string _activeSchemaProfileName = SchemaProfile.CoreProfileName;
    private UnitGeometrySource _unitGeometrySource = UnitGeometrySource.Unset;
    private UnitAttributeSource _unitAttributeSource = UnitAttributeSource.Unset;
    private List<ValidationPolicyProfile> _validationPolicyProfiles = ValidationPolicyProfile.NormalizeProfiles(null);
    private string _activeValidationPolicyProfileName = ValidationPolicyProfile.RecommendedProfileName;

    public ExportDialogWpf(
        IReadOnlyList<ViewPlan> views,
        ExportDialogSettings settings,
        IReadOnlyList<LinkSelectionItem>? availableLinks = null,
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

        foreach (LinkSelectionItem link in availableLinks ?? Array.Empty<LinkSelectionItem>())
        {
            if (link != null)
            {
                _links.Add(new LinkSelectionRow(link));
            }
        }

        _profiles = (profiles ?? Array.Empty<ExportProfile>()).ToList();
        _previewRequested = previewRequested;
        _openMappingsRequested = openMappingsRequested;
        _coordinateInfo = coordinateInfo;
        _previewBasemapSettings = new PreviewBasemapSettings(settings.PreviewBasemapUrlTemplate, settings.PreviewBasemapAttribution);
        _linkExportOptions = settings.LinkExportOptions?.Clone() ?? new LinkExportOptions();

        _window = new Window
        {
            Width = 980,
            Height = 760,
            MinWidth = 900,
            MinHeight = 680,
            Background = WindowBackgroundBrush,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = BuildLayout(),
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
            UnitSource = UnitExportSettingsResolver.ToLegacy(_unitGeometrySource, _unitAttributeSource),
            UnitGeometrySource = _unitGeometrySource,
            UnitAttributeSource = _unitAttributeSource,
            RoomCategoryParameterName = (_roomCategoryParameterTextBox.Text ?? string.Empty).Trim(),
            LinkExportOptions = BuildLinkExportOptions(),
            SchemaProfiles = _schemaProfiles.Select(profile => profile.Clone()).ToList(),
            ActiveSchemaProfileName = _activeSchemaProfileName,
            ValidationPolicyProfiles = _validationPolicyProfiles.Select(profile => profile.Clone()).ToList(),
            ActiveValidationPolicyProfileName = _activeValidationPolicyProfileName,
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

    private UIElement BuildLayout()
    {
        WpfGrid root = new()
        {
            Margin = new Thickness(16),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        WpfGrid content = new();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.55, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.45, GridUnitType.Star) });

        UIElement viewsCard = BuildViewsCard();
        if (viewsCard is FrameworkElement viewsElement)
        {
            viewsElement.Margin = new Thickness(0, 0, 14, 0);
        }

        content.Children.Add(viewsCard);

        UIElement optionsColumn = BuildOptionsColumn();
        WpfGrid.SetColumn(optionsColumn, 1);
        content.Children.Add(optionsColumn);

        root.Children.Add(content);

        UIElement footer = BuildFooter();
        WpfGrid.SetRow(footer, 1);
        root.Children.Add(footer);

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

    private DataTemplate BuildLinkSelectionTemplate()
    {
        FrameworkElementFactory checkBox = new(typeof(CheckBox));
        checkBox.SetBinding(CheckBox.ContentProperty, new System.Windows.Data.Binding(nameof(LinkSelectionRow.DisplayText)));
        checkBox.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding(nameof(LinkSelectionRow.IsSelected))
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

    private UIElement BuildViewsCard()
    {
        Border card = CreateCard();
        DockPanel layout = new() { LastChildFill = true };

        StackPanel header = new()
        {
            Margin = new Thickness(0, 0, 0, 12),
        };
        StyleSectionTitle(_viewsTitleText);
        StyleDescriptionText(_viewSelectionSummaryText);
        header.Children.Add(_viewsTitleText);
        header.Children.Add(_viewSelectionSummaryText);
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0),
        };
        _selectAllButton.MinWidth = 100;
        _selectAllButton.Padding = new Thickness(12, 6, 12, 6);
        _selectAllButton.Click += (_, _) => SetAllViewsSelected(true);
        actions.Children.Add(_selectAllButton);

        _clearAllButton.MinWidth = 100;
        _clearAllButton.Padding = new Thickness(12, 6, 12, 6);
        _clearAllButton.Margin = new Thickness(8, 0, 0, 0);
        _clearAllButton.Click += (_, _) => SetAllViewsSelected(false);
        actions.Children.Add(_clearAllButton);
        DockPanel.SetDock(actions, Dock.Bottom);
        layout.Children.Add(actions);

        _viewList.ItemsSource = _views;
        _viewList.ItemTemplate = BuildViewSelectionTemplate();
        _viewList.BorderThickness = new Thickness(0);
        _viewList.Background = Brushes.Transparent;
        _viewList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _viewList.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnInputChanged));
        _viewList.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnInputChanged));

        Border listBorder = new()
        {
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.White,
            Padding = new Thickness(4),
        };
        listBorder.Child = _viewList;
        layout.Children.Add(listBorder);

        card.Child = layout;
        return card;
    }

    private UIElement BuildOptionsColumn()
    {
        StackPanel stack = new();
        stack.Children.Add(BuildExportDestinationCard());
        stack.Children.Add(BuildIncludeCard());
        stack.Children.Add(BuildCoordinateCard());
        stack.Children.Add(BuildAdvancedCard());

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = stack,
        };
    }

    private UIElement BuildExportDestinationCard()
    {
        _outputDirectoryTextBox.MinHeight = 32;
        _outputDirectoryTextBox.TextChanged += (_, _) => RefreshDialogState();

        _browseButton.MinWidth = 100;
        _browseButton.Padding = new Thickness(12, 6, 12, 6);
        _browseButton.Margin = new Thickness(8, 0, 0, 0);
        _browseButton.Click += (_, _) => BrowseForOutputDirectory();

        WpfGrid pathGrid = new();
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathGrid.Children.Add(_outputDirectoryTextBox);
        WpfGrid.SetColumn(_browseButton, 1);
        pathGrid.Children.Add(_browseButton);

        return CreateSectionCard(
            _exportToTitleText,
            _exportToDescriptionText,
            CreateFieldBlock(_outputDirectoryLabel, pathGrid));
    }

    private UIElement BuildIncludeCard()
    {
        StackPanel panel = new();

        ConfigureFeatureCheckBox(_unitCheckBox, "Exports the `unit` layer.", "`unit` レイヤーを出力します。");
        ConfigureFeatureCheckBox(_detailCheckBox, "Exports the `detail` layer.", "`detail` レイヤーを出力します。");
        ConfigureFeatureCheckBox(_openingCheckBox, "Exports the `opening` layer.", "`opening` レイヤーを出力します。");
        ConfigureFeatureCheckBox(_levelCheckBox, "Exports the `level` layer.", "`level` レイヤーを出力します。");

        panel.Children.Add(_unitCheckBox);
        panel.Children.Add(_detailCheckBox);
        panel.Children.Add(_openingCheckBox);
        panel.Children.Add(_levelCheckBox);
        return CreateSectionCard(_includeTitleText, _includeDescriptionText, panel);
    }

    private UIElement BuildCoordinateCard()
    {
        StackPanel content = new();

        _coordinateStatusCard.BorderBrush = CardBorderBrush;
        _coordinateStatusCard.BorderThickness = new Thickness(1);
        _coordinateStatusCard.CornerRadius = new CornerRadius(6);
        _coordinateStatusCard.Background = StatusBackgroundBrush;
        _coordinateStatusCard.Padding = new Thickness(12);

        StackPanel statusPanel = new();
        _coordinateStatusTitleText.FontWeight = FontWeights.SemiBold;
        _coordinateStatusTitleText.Foreground = StatusTextBrush;
        _coordinateStatusTitleText.Margin = new Thickness(0, 0, 0, 4);
        _coordinateStatusText.TextWrapping = TextWrapping.Wrap;
        _coordinateStatusText.Foreground = StatusTextBrush;
        _coordinateStatusText.FontWeight = FontWeights.SemiBold;
        _coordinateStatusDetailText.TextWrapping = TextWrapping.Wrap;
        _coordinateStatusDetailText.Margin = new Thickness(0, 6, 0, 0);
        _coordinateStatusDetailText.Foreground = MutedTextBrush;
        statusPanel.Children.Add(_coordinateStatusTitleText);
        statusPanel.Children.Add(_coordinateStatusText);
        statusPanel.Children.Add(_coordinateStatusDetailText);
        _coordinateStatusCard.Child = statusPanel;
        content.Children.Add(_coordinateStatusCard);

        _coordinateModeComboBox.MinHeight = 32;
        _coordinateModeComboBox.SelectionChanged += (_, _) => RefreshDialogState();
        _presetComboBox.MinHeight = 32;
        _presetComboBox.SelectionChanged += (_, _) =>
        {
            if (_isInitializing)
            {
                return;
            }

            if (_presetComboBox.SelectedItem is CrsPresetItem item)
            {
                _targetEpsgTextBox.Text = item.Epsg.ToString();
            }

            RefreshDialogState();
        };
        _targetEpsgTextBox.MinHeight = 32;
        _targetEpsgTextBox.TextChanged += (_, _) => RefreshDialogState();

        StyleExpanderHeader(_coordinateSettingsHeaderText);
        _coordinateSettingsExpander.Header = _coordinateSettingsHeaderText;
        _coordinateSettingsExpander.Margin = new Thickness(0, 12, 0, 0);
        StackPanel coordinateSettingsContent = new();
        coordinateSettingsContent.Children.Add(CreateFieldBlock(_coordinateModeLabel, _coordinateModeComboBox));
        _convertSettingsPanel.Children.Add(CreateFieldBlock(_crsPresetLabel, _presetComboBox));
        _convertSettingsPanel.Children.Add(CreateFieldBlock(_targetEpsgLabel, _targetEpsgTextBox, 0));
        coordinateSettingsContent.Children.Add(_convertSettingsPanel);
        _coordinateSettingsExpander.Content = coordinateSettingsContent;
        content.Children.Add(_coordinateSettingsExpander);

        StyleExpanderHeader(_technicalDetailsHeaderText);
        _technicalDetailsExpander.Header = _technicalDetailsHeaderText;
        _technicalDetailsExpander.Margin = new Thickness(0, 8, 0, 0);
        StackPanel technicalDetailsContent = new();
        ConfigureInfoLine(_displayUnitsInfoText);
        ConfigureInfoLine(_projectLocationInfoText);
        ConfigureInfoLine(_siteCrsInfoText);
        ConfigureInfoLine(_sharedCoordinateInfoText, 0);
        technicalDetailsContent.Children.Add(_displayUnitsInfoText);
        technicalDetailsContent.Children.Add(_projectLocationInfoText);
        technicalDetailsContent.Children.Add(_siteCrsInfoText);
        technicalDetailsContent.Children.Add(_sharedCoordinateInfoText);
        _technicalDetailsExpander.Content = technicalDetailsContent;
        content.Children.Add(_technicalDetailsExpander);

        return CreateSectionCard(_coordinateTitleText, _coordinateDescriptionText, content);
    }

    private UIElement BuildAdvancedCard()
    {
        StackPanel advancedContent = new();

        _unitSourceComboBox.MinHeight = 32;
        _unitSourceComboBox.SelectionChanged += (_, _) =>
        {
            UnitSource source = (_unitSourceComboBox.SelectedItem as UnitSourceItem)?.Source ?? UnitSource.Floors;
            _unitGeometrySource = source == UnitSource.Rooms
                ? UnitGeometrySource.Rooms
                : UnitGeometrySource.Floors;
            if (_unitGeometrySource == UnitGeometrySource.Rooms &&
                _unitAttributeSource == UnitAttributeSource.Hybrid)
            {
                _unitAttributeSource = UnitAttributeSource.Rooms;
                SelectUnitAttributeSource(_unitAttributeSource);
            }

            SyncLegacyUnitSource();
            RefreshDialogState();
        };
        _unitAttributeSourceComboBox.MinHeight = 32;
        _unitAttributeSourceComboBox.SelectionChanged += (_, _) =>
        {
            _unitAttributeSource = (_unitAttributeSourceComboBox.SelectedItem as UnitAttributeSourceItem)?.Source ?? UnitAttributeSource.Hybrid;
            SyncLegacyUnitSource();
            RefreshDialogState();
        };
        _roomCategoryParameterTextBox.MinHeight = 32;
        _roomCategoryParameterTextBox.TextChanged += (_, _) => RefreshDialogState();
        _schemaProfileComboBox.MinHeight = 32;
        _schemaProfileComboBox.SelectionChanged += (_, _) =>
        {
            if (_schemaProfileComboBox.SelectedItem is SchemaProfileItem selected)
            {
                _activeSchemaProfileName = selected.Profile.Name;
            }

            RefreshDialogState();
        };
        _validationPolicyComboBox.MinHeight = 32;
        _validationPolicyComboBox.SelectionChanged += (_, _) =>
        {
            if (_validationPolicyComboBox.SelectedItem is ValidationPolicyProfileItem selected)
            {
                _activeValidationPolicyProfileName = selected.Profile.Name;
            }

            RefreshDialogState();
        };
        _manageSchemaProfilesButton.HorizontalAlignment = HorizontalAlignment.Left;
        _manageSchemaProfilesButton.Padding = new Thickness(12, 6, 12, 6);
        _manageSchemaProfilesButton.Margin = new Thickness(8, 0, 0, 0);
        _manageSchemaProfilesButton.Click += (_, _) => EditSchemaProfiles();
        _manageValidationPoliciesButton.HorizontalAlignment = HorizontalAlignment.Left;
        _manageValidationPoliciesButton.Padding = new Thickness(12, 6, 12, 6);
        _manageValidationPoliciesButton.Margin = new Thickness(8, 0, 0, 0);
        _manageValidationPoliciesButton.Click += (_, _) => EditValidationPolicies();
        _includeLinkedModelsCheckBox.Margin = new Thickness(0, 0, 0, 8);
        _includeLinkedModelsCheckBox.Padding = new Thickness(2);
        _includeLinkedModelsCheckBox.Checked += (_, _) => RefreshDialogState();
        _includeLinkedModelsCheckBox.Unchecked += (_, _) => RefreshDialogState();

        _linkList.ItemsSource = _links;
        _linkList.ItemTemplate = BuildLinkSelectionTemplate();
        _linkList.BorderThickness = new Thickness(1);
        _linkList.BorderBrush = CardBorderBrush;
        _linkList.Background = Brushes.White;
        _linkList.MinHeight = 92;
        _linkList.MaxHeight = 168;
        _linkList.Padding = new Thickness(4);
        _linkList.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnInputChanged));
        _linkList.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnInputChanged));

        _unitSourceRow = CreateFieldBlock(_unitSourceLabel, _unitSourceComboBox);
        _unitAttributeSourceRow = CreateFieldBlock(_unitAttributeSourceLabel, _unitAttributeSourceComboBox);
        _roomCategoryParameterRow = CreateFieldBlock(_roomCategoryParameterLabel, _roomCategoryParameterTextBox);
        StackPanel schemaProfilePanel = new()
        {
            Orientation = Orientation.Horizontal,
        };
        _schemaProfileComboBox.MinWidth = 200;
        schemaProfilePanel.Children.Add(_schemaProfileComboBox);
        schemaProfilePanel.Children.Add(_manageSchemaProfilesButton);
        _schemaProfileRow = CreateFieldBlock(_schemaProfileLabel, schemaProfilePanel);
        StackPanel validationPolicyPanel = new()
        {
            Orientation = Orientation.Horizontal,
        };
        _validationPolicyComboBox.MinWidth = 200;
        validationPolicyPanel.Children.Add(_validationPolicyComboBox);
        validationPolicyPanel.Children.Add(_manageValidationPoliciesButton);
        _validationPolicyRow = CreateFieldBlock(_validationPolicyLabel, validationPolicyPanel);
        StackPanel linkedModelsPanel = new();
        linkedModelsPanel.Children.Add(_includeLinkedModelsCheckBox);
        linkedModelsPanel.Children.Add(_linkList);
        _linkedModelsRow = CreateFieldBlock(_linkedModelsLabel, linkedModelsPanel);
        advancedContent.Children.Add(_unitSourceRow);
        advancedContent.Children.Add(_unitAttributeSourceRow);
        advancedContent.Children.Add(_roomCategoryParameterRow);
        advancedContent.Children.Add(_schemaProfileRow);
        advancedContent.Children.Add(_validationPolicyRow);
        advancedContent.Children.Add(_linkedModelsRow);

        _mappingsButton.HorizontalAlignment = HorizontalAlignment.Left;
        _mappingsButton.Padding = new Thickness(12, 6, 12, 6);
        _mappingsButton.Margin = new Thickness(0, 0, 0, 12);
        _mappingsButton.Click += (_, _) => _openMappingsRequested?.Invoke();
        advancedContent.Children.Add(_mappingsButton);

        ConfigureAdvancedOption(_diagnosticsCheckBox);
        ConfigureAdvancedOption(_packageCheckBox);
        _packageCheckBox.Checked += (_, _) => RefreshDialogState();
        _packageCheckBox.Unchecked += (_, _) => RefreshDialogState();
        ConfigureAdvancedOption(_packageLegendCheckBox);

        advancedContent.Children.Add(WrapStandaloneOption(_diagnosticsCheckBox, new Thickness(0, 0, 0, 4)));
        advancedContent.Children.Add(WrapStandaloneOption(_packageCheckBox, new Thickness(0, 0, 0, 4)));
        _legendOptionRow = WrapStandaloneOption(_packageLegendCheckBox, new Thickness(18, 0, 0, 0));
        advancedContent.Children.Add(_legendOptionRow);

        StyleExpanderHeader(_advancedOptionsHeaderText);
        _advancedOptionsExpander.Header = _advancedOptionsHeaderText;
        _advancedOptionsExpander.IsExpanded = false;
        _advancedOptionsExpander.Content = advancedContent;

        return CreateSectionCard(_advancedTitleText, _advancedDescriptionText, _advancedOptionsExpander);
    }

    private UIElement BuildFooter()
    {
        Border footerBorder = new()
        {
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(0, 12, 0, 0),
        };

        DockPanel footer = new() { LastChildFill = false };

        StackPanel left = new();
        _footerSummaryText.FontWeight = FontWeights.SemiBold;
        _footerSummaryText.Foreground = StatusTextBrush;
        _footerSummaryText.TextWrapping = TextWrapping.Wrap;
        left.Children.Add(_footerSummaryText);

        StackPanel meta = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _versionText.Foreground = MutedTextBrush;
        _versionText.VerticalAlignment = VerticalAlignment.Center;
        meta.Children.Add(_versionText);

        _helpButton.Padding = new Thickness(10, 4, 10, 4);
        _helpButton.Margin = new Thickness(12, 0, 0, 0);
        _helpButton.Click += (_, _) => ShowHelp();
        meta.Children.Add(_helpButton);

        _languageLabel.Margin = new Thickness(16, 0, 8, 0);
        _languageLabel.Foreground = MutedTextBrush;
        _languageLabel.VerticalAlignment = VerticalAlignment.Center;
        meta.Children.Add(_languageLabel);

        _languageComboBox.MinWidth = 120;
        _languageComboBox.SelectionChanged += (_, _) => OnLanguageChanged();
        meta.Children.Add(_languageComboBox);

        left.Children.Add(meta);
        DockPanel.SetDock(left, Dock.Left);
        footer.Children.Add(left);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        DockPanel.SetDock(actions, Dock.Right);

        _cancelButton.MinWidth = 96;
        _cancelButton.Padding = new Thickness(12, 6, 12, 6);
        _cancelButton.IsCancel = true;
        _cancelButton.Click += (_, _) =>
        {
            _window.DialogResult = false;
            _window.Close();
        };
        actions.Children.Add(_cancelButton);

        _previewButton.MinWidth = 110;
        _previewButton.Padding = new Thickness(12, 6, 12, 6);
        _previewButton.Margin = new Thickness(8, 0, 0, 0);
        _previewButton.Click += (_, _) => ShowPreview();
        actions.Children.Add(_previewButton);

        _exportButton.MinWidth = 110;
        _exportButton.Padding = new Thickness(12, 6, 12, 6);
        _exportButton.Margin = new Thickness(8, 0, 0, 0);
        _exportButton.IsDefault = true;
        _exportButton.Click += (_, _) => ConfirmExport();
        actions.Children.Add(_exportButton);

        footer.Children.Add(actions);
        footerBorder.Child = footer;
        return footerBorder;
    }

    private void LoadValues(ExportDialogSettings settings)
    {
        _isInitializing = true;
        try
        {
            _language = Enum.IsDefined(typeof(UiLanguage), settings.UiLanguage)
                ? settings.UiLanguage
                : UiLanguage.English;
            UpdateDisplayLanguages();

            _outputDirectoryTextBox.Text = string.IsNullOrWhiteSpace(settings.OutputDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : settings.OutputDirectory;
            _targetEpsgTextBox.Text = (settings.TargetEpsg > 0
                ? settings.TargetEpsg
                : (_coordinateInfo?.ResolvedSourceEpsg ?? ProjectInfo.DefaultTargetEpsg)).ToString();

            ExportFeatureType featureTypes = settings.FeatureTypes == ExportFeatureType.None
                ? ExportFeatureType.All
                : settings.FeatureTypes;
            _unitCheckBox.IsChecked = featureTypes.HasFlag(ExportFeatureType.Unit);
            _detailCheckBox.IsChecked = featureTypes.HasFlag(ExportFeatureType.Detail);
            _openingCheckBox.IsChecked = featureTypes.HasFlag(ExportFeatureType.Opening);
            _levelCheckBox.IsChecked = featureTypes.HasFlag(ExportFeatureType.Level);

            _diagnosticsCheckBox.IsChecked = settings.GenerateDiagnosticsReport;
            _packageCheckBox.IsChecked = settings.GeneratePackageOutput;
            _packageLegendCheckBox.IsChecked = settings.IncludePackageLegend;

            _languageComboBox.Items.Clear();
            _languageComboBox.Items.Add(new LanguageItem(UiLanguage.English));
            _languageComboBox.Items.Add(new LanguageItem(UiLanguage.Japanese));
            _languageComboBox.SelectedIndex = _language == UiLanguage.Japanese ? 1 : 0;

            _coordinateModeComboBox.Items.Clear();
            _coordinateModeComboBox.Items.Add(new CoordinateModeItem(CoordinateExportMode.SharedCoordinates));
            _coordinateModeComboBox.Items.Add(new CoordinateModeItem(CoordinateExportMode.ConvertToTargetCrs));
            _coordinateModeComboBox.SelectedIndex = settings.CoordinateMode == CoordinateExportMode.ConvertToTargetCrs ? 1 : 0;

            _presetComboBox.Items.Clear();
            foreach (KeyValuePair<int, string> zone in JapanPlaneRectangular.Zones.OrderBy(entry => entry.Key))
            {
                CrsPresetItem item = new(zone.Key, zone.Value);
                _presetComboBox.Items.Add(item);
                if (zone.Key == settings.TargetEpsg)
                {
                    _presetComboBox.SelectedItem = item;
                }
            }

            _unitSourceComboBox.Items.Clear();
            _unitSourceComboBox.Items.Add(new UnitSourceItem(UnitSource.Floors));
            _unitSourceComboBox.Items.Add(new UnitSourceItem(UnitSource.Rooms));
            _unitAttributeSourceComboBox.Items.Clear();
            _unitAttributeSourceComboBox.Items.Add(new UnitAttributeSourceItem(UnitAttributeSource.Floors));
            _unitAttributeSourceComboBox.Items.Add(new UnitAttributeSourceItem(UnitAttributeSource.Rooms));
            _unitAttributeSourceComboBox.Items.Add(new UnitAttributeSourceItem(UnitAttributeSource.Hybrid));
            _unitGeometrySource = UnitExportSettingsResolver.ResolveGeometrySource(settings.UnitSource, settings.UnitGeometrySource);
            _unitAttributeSource = UnitExportSettingsResolver.ResolveAttributeSource(settings.UnitSource, _unitGeometrySource, settings.UnitAttributeSource);
            _unitSource = UnitExportSettingsResolver.ToLegacy(_unitGeometrySource, _unitAttributeSource);
            _unitSourceComboBox.SelectedIndex = UnitExportSettingsResolver.ToLegacy(_unitGeometrySource, _unitAttributeSource) == UnitSource.Rooms ? 1 : 0;
            SelectUnitAttributeSource(_unitAttributeSource);
            _roomCategoryParameterTextBox.Text = string.IsNullOrWhiteSpace(settings.RoomCategoryParameterName)
                ? "Name"
                : settings.RoomCategoryParameterName.Trim();
            _linkExportOptions = settings.LinkExportOptions?.Clone() ?? new LinkExportOptions();
            _schemaProfiles = SchemaProfile.NormalizeProfiles(settings.SchemaProfiles).Select(profile => profile.Clone()).ToList();
            _activeSchemaProfileName = SchemaProfile.ResolveActiveName(_schemaProfiles, settings.ActiveSchemaProfileName);
            _validationPolicyProfiles = ValidationPolicyProfile.NormalizeProfiles(settings.ValidationPolicyProfiles).Select(profile => profile.Clone()).ToList();
            _activeValidationPolicyProfileName = ValidationPolicyProfile.ResolveActiveName(_validationPolicyProfiles, settings.ActiveValidationPolicyProfileName);
            PopulateSchemaProfiles();
            PopulateValidationPolicies();
            ApplyLinkSelections(_linkExportOptions);

            HashSet<long> selectedIds = new(settings.SelectedViewIds ?? new List<long>());
            bool selectAll = selectedIds.Count == 0;
            foreach (ViewSelectionRow row in _views)
            {
                row.IsSelected = selectAll || selectedIds.Contains(row.View.Id.Value);
            }

            _coordinateSettingsExpander.IsExpanded = settings.CoordinateMode == CoordinateExportMode.ConvertToTargetCrs;
            _technicalDetailsExpander.IsExpanded = false;
            _advancedOptionsExpander.IsExpanded = false;

            _viewList.Items.Refresh();
            _unitSourceComboBox.Items.Refresh();
            _unitAttributeSourceComboBox.Items.Refresh();
            _presetComboBox.Items.Refresh();
            _languageComboBox.Items.Refresh();
            _coordinateModeComboBox.Items.Refresh();
            ApplyLanguage();
        }
        finally
        {
            _isInitializing = false;
        }

        RefreshDialogState();
    }

    private void ConfirmExport()
    {
        List<ViewPlan> selectedViews = GetSelectedViews();
        if (selectedViews.Count == 0)
        {
            MessageBox.Show(
                _window,
                T("Select at least one plan view to export.", "出力する平面ビューを 1 つ以上選択してください。"),
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
                T("Select at least one item to include in the export.", "出力内容を 1 つ以上選択してください。"),
                _window.Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string outputDirectory = (_outputDirectoryTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            MessageBox.Show(
                _window,
                T("Choose an output directory.", "出力先フォルダーを選択してください。"),
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
                    T("Enter a valid EPSG code.", "有効な EPSG コードを入力してください。"),
                    _window.Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_coordinateInfo?.CanConvert != true)
            {
                MessageBox.Show(
                    _window,
                    T(
                        "Conversion requires a recognizable shared/site coordinate system in the current Revit model.",
                        "座標変換には、現在の Revit モデルで認識可能な共有 / サイト座標系が必要です。"),
                    _window.Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        UiLanguage uiLanguage = (_languageComboBox.SelectedItem as LanguageItem)?.Language ?? UiLanguage.English;
        SyncLegacyUnitSource();
        UnitSource unitSource = _unitSource;

        Result = new ExportDialogResult(
            selectedViews,
            outputDirectory,
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
            _unitGeometrySource,
            _unitAttributeSource,
            (_roomCategoryParameterTextBox.Text ?? string.Empty).Trim(),
            BuildLinkExportOptions(),
            GetActiveSchemaProfile(),
            GetActiveValidationPolicyProfile());

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
                T("Select at least one plan view to preview.", "プレビューする平面ビューを 1 つ以上選択してください。"),
                _window.Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (GetSelectedFeatureTypes() == ExportFeatureType.None)
        {
            MessageBox.Show(
                _window,
                T("Preview requires at least one selected export item.", "プレビューには出力内容を 1 つ以上選択する必要があります。"),
                _window.Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SyncLegacyUnitSource();
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
            _unitSource,
            _unitGeometrySource,
            _unitAttributeSource,
            (_roomCategoryParameterTextBox.Text ?? string.Empty).Trim(),
            BuildLinkExportOptions(),
            GetActiveSchemaProfile(),
            _previewBasemapSettings.UrlTemplate,
            _previewBasemapSettings.Attribution);

        try
        {
            _previewRequested(previewRequest, TryGetOwnerWindow());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _window,
                $"{T("Preview could not be opened.", "プレビューを開けませんでした。")}\n\n{ex.Message}",
                _window.Title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        _window.Activate();
    }

    private void BrowseForOutputDirectory()
    {
        using WinForms.FolderBrowserDialog folderDialog = new()
        {
            Description = T("Select output folder for GeoPackage files", "GeoPackage ファイルの出力先フォルダーを選択してください。"),
            ShowNewFolderButton = true,
            SelectedPath = (_outputDirectoryTextBox.Text ?? string.Empty).Trim(),
        };

        WinForms.DialogResult result = TryGetOwnerWindow() is WinForms.IWin32Window owner
            ? folderDialog.ShowDialog(owner)
            : folderDialog.ShowDialog();

        if (result == WinForms.DialogResult.OK)
        {
            _outputDirectoryTextBox.Text = folderDialog.SelectedPath;
        }
    }

    private void ShowHelp()
    {
        HelpLauncher.Show(TryGetOwnerWindow(), HelpTopic.ExportWorkflow, _language, _window.Title);
        _window.Activate();
    }

    private WinForms.IWin32Window? TryGetOwnerWindow()
    {
        IntPtr handle = new WindowInteropHelper(_window).EnsureHandle();
        return handle == IntPtr.Zero ? null : new Win32WindowOwner(handle);
    }

    private void SetAllViewsSelected(bool isSelected)
    {
        foreach (ViewSelectionRow row in _views)
        {
            row.IsSelected = isSelected;
        }

        _viewList.Items.Refresh();
        RefreshDialogState();
    }

    private void OnLanguageChanged()
    {
        if (_isInitializing || _languageComboBox.SelectedItem is not LanguageItem item)
        {
            return;
        }

        _language = item.Language;
        UpdateDisplayLanguages();
        _viewList.Items.Refresh();
        _unitSourceComboBox.Items.Refresh();
        _unitAttributeSourceComboBox.Items.Refresh();
        _presetComboBox.Items.Refresh();
        _languageComboBox.Items.Refresh();
        _coordinateModeComboBox.Items.Refresh();
        ApplyLanguage();
        RefreshDialogState();
    }

    private void OnInputChanged(object? sender, RoutedEventArgs e)
    {
        RefreshDialogState();
    }

    private void RefreshDialogState()
    {
        if (_isInitializing)
        {
            return;
        }

        UpdateViewSelectionSummary();
        UpdateAdvancedOptionsVisibility();
        UpdateCoordinateModeUi();
        UpdateFooterSummary();
        UpdateActionButtons();
    }

    private void UpdateViewSelectionSummary()
    {
        _viewSelectionSummaryText.Text = TF("{0} of {1} selected", "{1} 件中 {0} 件を選択", GetSelectedViews().Count, _views.Count);
    }

    private void UpdateAdvancedOptionsVisibility()
    {
        bool unitsEnabled = _unitCheckBox.IsChecked == true;
        bool usesRoomAssignments = UnitExportSettingsResolver.UsesRoomCategoryAssignments(_unitAttributeSource);
        bool packageEnabled = _packageCheckBox.IsChecked == true;
        bool linksEnabled = _includeLinkedModelsCheckBox.IsChecked == true && _links.Count > 0;

        if (_unitSourceRow != null)
        {
            _unitSourceRow.Visibility = unitsEnabled ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        if (_unitAttributeSourceRow != null)
        {
            _unitAttributeSourceRow.Visibility = unitsEnabled ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        if (_roomCategoryParameterRow != null)
        {
            _roomCategoryParameterRow.Visibility = unitsEnabled && usesRoomAssignments
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        }

        if (_legendOptionRow != null)
        {
            _legendOptionRow.Visibility = packageEnabled ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        _includeLinkedModelsCheckBox.IsEnabled = _links.Count > 0;
        _linkList.IsEnabled = linksEnabled;
    }

    private List<ViewPlan> GetSelectedViews() => _views.Where(x => x.IsSelected).Select(x => x.View).ToList();

    private void ApplyLinkSelections(LinkExportOptions linkExportOptions)
    {
        HashSet<long> selectedIds = new(linkExportOptions.SelectedLinkInstanceIds ?? new List<long>());
        foreach (LinkSelectionRow row in _links)
        {
            row.IsSelected = linkExportOptions.IncludeLinkedModels && selectedIds.Contains(row.Link.LinkInstanceId);
        }

        _includeLinkedModelsCheckBox.IsChecked = linkExportOptions.IncludeLinkedModels && _links.Count > 0;
        _linkList.Items.Refresh();
    }

    private void PopulateSchemaProfiles()
    {
        _schemaProfiles = SchemaProfile.NormalizeProfiles(_schemaProfiles).Select(profile => profile.Clone()).ToList();
        _activeSchemaProfileName = SchemaProfile.ResolveActiveName(_schemaProfiles, _activeSchemaProfileName);

        _schemaProfileComboBox.Items.Clear();
        foreach (SchemaProfile profile in _schemaProfiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase))
        {
            _schemaProfileComboBox.Items.Add(new SchemaProfileItem(profile));
        }

        for (int i = 0; i < _schemaProfileComboBox.Items.Count; i++)
        {
            if (_schemaProfileComboBox.Items[i] is SchemaProfileItem item &&
                string.Equals(item.Profile.Name, _activeSchemaProfileName, StringComparison.OrdinalIgnoreCase))
            {
                _schemaProfileComboBox.SelectedIndex = i;
                return;
            }
        }

        _schemaProfileComboBox.SelectedIndex = _schemaProfileComboBox.Items.Count > 0 ? 0 : -1;
    }

    private void PopulateValidationPolicies()
    {
        _validationPolicyProfiles = ValidationPolicyProfile.NormalizeProfiles(_validationPolicyProfiles)
            .Select(profile => profile.Clone())
            .ToList();
        _activeValidationPolicyProfileName = ValidationPolicyProfile.ResolveActiveName(
            _validationPolicyProfiles,
            _activeValidationPolicyProfileName);

        _validationPolicyComboBox.Items.Clear();
        foreach (ValidationPolicyProfile profile in _validationPolicyProfiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase))
        {
            _validationPolicyComboBox.Items.Add(new ValidationPolicyProfileItem(profile));
        }

        for (int i = 0; i < _validationPolicyComboBox.Items.Count; i++)
        {
            if (_validationPolicyComboBox.Items[i] is ValidationPolicyProfileItem item &&
                string.Equals(item.Profile.Name, _activeValidationPolicyProfileName, StringComparison.OrdinalIgnoreCase))
            {
                _validationPolicyComboBox.SelectedIndex = i;
                return;
            }
        }

        _validationPolicyComboBox.SelectedIndex = _validationPolicyComboBox.Items.Count > 0 ? 0 : -1;
    }

    private SchemaProfile GetActiveSchemaProfile()
    {
        return SchemaProfile.ResolveActive(_schemaProfiles, _activeSchemaProfileName);
    }

    private ValidationPolicyProfile GetActiveValidationPolicyProfile()
    {
        return ValidationPolicyProfile.NormalizeProfiles(_validationPolicyProfiles)
            .FirstOrDefault(profile => string.Equals(profile.Name, _activeValidationPolicyProfileName, StringComparison.OrdinalIgnoreCase))
            ?.Clone() ?? ValidationPolicyProfile.CreateRecommendedProfile();
    }

    private void EditSchemaProfiles()
    {
        using SchemaProfileManagerForm form = new(_schemaProfiles, _language);
        if (TryGetOwnerWindow() is WinForms.IWin32Window owner)
        {
            if (form.ShowDialog(owner) != WinForms.DialogResult.OK)
            {
                return;
            }
        }
        else if (form.ShowDialog() != WinForms.DialogResult.OK)
        {
            return;
        }

        _schemaProfiles = SchemaProfile.NormalizeProfiles(form.Profiles).Select(profile => profile.Clone()).ToList();
        _activeSchemaProfileName = SchemaProfile.ResolveActiveName(_schemaProfiles, _activeSchemaProfileName);
        PopulateSchemaProfiles();
    }

    private void EditValidationPolicies()
    {
        using ValidationPolicyManagerForm form = new(_validationPolicyProfiles, _language);
        if (TryGetOwnerWindow() is WinForms.IWin32Window owner)
        {
            if (form.ShowDialog(owner) != WinForms.DialogResult.OK)
            {
                return;
            }
        }
        else if (form.ShowDialog() != WinForms.DialogResult.OK)
        {
            return;
        }

        _validationPolicyProfiles = ValidationPolicyProfile.NormalizeProfiles(form.Profiles)
            .Select(profile => profile.Clone())
            .ToList();
        _activeValidationPolicyProfileName = ValidationPolicyProfile.ResolveActiveName(
            _validationPolicyProfiles,
            _activeValidationPolicyProfileName);
        PopulateValidationPolicies();
    }

    private LinkExportOptions BuildLinkExportOptions()
    {
        if (_links.Count == 0 || _includeLinkedModelsCheckBox.IsChecked != true)
        {
            return new LinkExportOptions();
        }

        return new LinkExportOptions
        {
            IncludeLinkedModels = true,
            SelectedLinkInstanceIds = _links
                .Where(link => link.IsSelected)
                .Select(link => link.Link.LinkInstanceId)
                .Distinct()
                .ToList(),
        };
    }

    private void SelectUnitAttributeSource(UnitAttributeSource source)
    {
        for (int i = 0; i < _unitAttributeSourceComboBox.Items.Count; i++)
        {
            if (_unitAttributeSourceComboBox.Items[i] is UnitAttributeSourceItem item && item.Source == source)
            {
                _unitAttributeSourceComboBox.SelectedIndex = i;
                return;
            }
        }

        _unitAttributeSourceComboBox.SelectedIndex = 0;
    }

    private void SyncLegacyUnitSource()
    {
        _unitSource = UnitExportSettingsResolver.ToLegacy(_unitGeometrySource, _unitAttributeSource);
    }

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
        _convertSettingsPanel.Visibility = isConvertMode ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        UpdateCoordinateInfoText(isConvertMode);
    }

    private void UpdateCoordinateInfoText(bool isConvertMode)
    {
        string displayUnits = string.IsNullOrWhiteSpace(_coordinateInfo?.DisplayLengthUnitLabel)
            ? T("Unknown", "不明")
            : _coordinateInfo!.DisplayLengthUnitLabel;
        string locationName = string.IsNullOrWhiteSpace(_coordinateInfo?.ActiveProjectLocationName)
            ? T("Unavailable", "利用不可")
            : _coordinateInfo!.ActiveProjectLocationName;
        string sharedCrs = string.IsNullOrWhiteSpace(_coordinateInfo?.ResolvedSourceLabel)
            ? T("Shared CRS not resolved", "共有 CRS を特定できません")
            : _coordinateInfo!.ResolvedSourceLabel;
        string siteId = string.IsNullOrWhiteSpace(_coordinateInfo?.SiteCoordinateSystemId)
            ? T("Not set", "未設定")
            : _coordinateInfo!.SiteCoordinateSystemId;
        string sharedSummary = string.IsNullOrWhiteSpace(_coordinateInfo?.SharedCoordinateSummary)
            ? T("Unavailable", "利用不可")
            : _coordinateInfo!.SharedCoordinateSummary;

        _displayUnitsInfoText.Text = TF("Model display units: {0}", "モデル表示単位: {0}", displayUnits);
        _projectLocationInfoText.Text = TF("Active project location: {0}", "アクティブなプロジェクト位置: {0}", locationName);
        _siteCrsInfoText.Text = TF("Shared CRS: {0} (Site CRS ID: {1})", "共有 CRS: {0} (サイト CRS ID: {1})", sharedCrs, siteId);
        _sharedCoordinateInfoText.Text = TF("Shared origin / transform: {0}", "共有原点 / 変換: {0}", sharedSummary);

        if (isConvertMode)
        {
            _coordinateStatusTitleText.Text = T("Coordinate conversion", "座標変換");
            if (_coordinateInfo?.CanConvert == true)
            {
                _coordinateStatusText.Text = TF("Converting from {0} to EPSG:{1}", "{0} から EPSG:{1} に変換して出力します", sharedCrs, ParseTargetEpsgOrDefault());
                _coordinateStatusDetailText.Text = TF("Active location: {0}", "アクティブな位置: {0}", locationName);
                ApplyCoordinateStatusStyle(false);
            }
            else
            {
                _coordinateStatusText.Text = T(
                    "Conversion is unavailable because the model does not have a recognizable shared/site CRS.",
                    "モデルに認識可能な共有 / サイト CRS がないため、座標変換は使用できません。");
                _coordinateStatusDetailText.Text = TF("Current shared CRS: {0}", "現在の共有 CRS: {0}", sharedCrs);
                ApplyCoordinateStatusStyle(true);
            }
        }
        else
        {
            _coordinateStatusTitleText.Text = T("Current export coordinates", "現在の出力座標");
            _coordinateStatusText.Text = TF("Using shared coordinates: {0}", "共有座標を使用: {0}", sharedCrs);
            _coordinateStatusDetailText.Text = TF("Active location: {0}", "アクティブな位置: {0}", locationName);
            ApplyCoordinateStatusStyle(false);
        }
    }

    private void ApplyCoordinateStatusStyle(bool warning)
    {
        _coordinateStatusCard.Background = warning ? StatusWarningBackgroundBrush : StatusBackgroundBrush;
        _coordinateStatusCard.BorderBrush = warning ? StatusWarningBorderBrush : CardBorderBrush;
        Brush textBrush = warning ? StatusWarningTextBrush : StatusTextBrush;
        _coordinateStatusTitleText.Foreground = textBrush;
        _coordinateStatusText.Foreground = textBrush;
        _coordinateStatusDetailText.Foreground = warning ? StatusWarningTextBrush : MutedTextBrush;
    }

    private void UpdateFooterSummary()
    {
        int selectedViewCount = GetSelectedViews().Count;
        int selectedLinkCount = BuildLinkExportOptions().SelectedLinkInstanceIds.Count;
        bool isConvertMode = GetSelectedCoordinateMode() == CoordinateExportMode.ConvertToTargetCrs;
        string modeText = isConvertMode
            ? T("Convert to target CRS", "出力 CRS に変換")
            : T("Shared coordinates", "共有座標");
        int epsg = isConvertMode
            ? ParseTargetEpsgOrDefault()
            : (_coordinateInfo?.ResolvedSourceEpsg ?? ParseTargetEpsgOrDefault());

        _footerSummaryText.Text = TF(
            "{0} views selected | {1} linked models | {2} | EPSG {3}",
            "{0} ビュー選択 | リンク モデル {1} 件 | {2} | EPSG {3}",
            selectedViewCount,
            selectedLinkCount,
            modeText,
            epsg);
    }

    private void UpdateActionButtons()
    {
        bool hasViews = GetSelectedViews().Count > 0;
        bool hasFeatureTypes = GetSelectedFeatureTypes() != ExportFeatureType.None;
        bool hasOutputDirectory = !string.IsNullOrWhiteSpace((_outputDirectoryTextBox.Text ?? string.Empty).Trim());

        _previewButton.IsEnabled = _previewRequested != null && hasViews && hasFeatureTypes;
        _exportButton.IsEnabled = hasViews && hasFeatureTypes && hasOutputDirectory;
    }

    private void ApplyLanguage()
    {
        _window.Title = T("Export GeoPackage", "GeoPackage を出力");
        _viewsTitleText.Text = T("Plan views", "平面ビュー");
        _exportToTitleText.Text = T("Export to", "出力先");
        _exportToDescriptionText.Text = T("Choose where the GeoPackage files should be written.", "GeoPackage ファイルの出力先を選択します。");
        _includeTitleText.Text = T("Include in export", "出力内容");
        _includeDescriptionText.Text = T("Choose which layers to include in this export.", "この出力に含めるレイヤーを選択します。");
        _coordinateTitleText.Text = T("Coordinate system", "座標系");
        _coordinateDescriptionText.Text = T("Review how this export will use model coordinates.", "この出力で使用する座標系を確認します。");
        _advancedTitleText.Text = T("Advanced options", "詳細オプション");
        _advancedDescriptionText.Text = T("Change settings used less often.", "使用頻度の低い設定を変更します。");

        _outputDirectoryLabel.Text = T("Output directory", "出力先フォルダー");
        _coordinateModeLabel.Text = T("Coordinate mode", "座標モード");
        _crsPresetLabel.Text = T("CRS preset", "CRS プリセット");
        _targetEpsgLabel.Text = T("Target EPSG", "出力 EPSG");
        _coordinateSettingsHeaderText.Text = T("Change coordinate settings", "座標設定を変更");
        _technicalDetailsHeaderText.Text = T("View technical details", "技術詳細を表示");
        _advancedOptionsHeaderText.Text = T("Show advanced options", "詳細オプションを表示");
        _unitSourceLabel.Text = T("Unit geometry source", "ユニット形状の取得元");
        _unitAttributeSourceLabel.Text = T("Unit attribute source", "ユニット属性の取得元");
        _roomCategoryParameterLabel.Text = T("Room category parameter", "部屋カテゴリ パラメータ");
        _schemaProfileLabel.Text = T("Schema profile", "スキーマ プロファイル");
        _validationPolicyLabel.Text = T("Validation policy", "検証ポリシー");
        _linkedModelsLabel.Text = T("Linked models", "リンク モデル");

        _unitCheckBox.Content = T("Units", "ユニット");
        _detailCheckBox.Content = T("Details", "ディテール");
        _openingCheckBox.Content = T("Openings", "開口");
        _levelCheckBox.Content = T("Levels", "レベル");
        _diagnosticsCheckBox.Content = T("Create diagnostics report", "診断レポートを作成");
        _packageCheckBox.Content = T("Create GIS package folder", "GIS パッケージ フォルダーを作成");
        _packageLegendCheckBox.Content = T("Include legend file", "凡例ファイルを含める");
        _includeLinkedModelsCheckBox.Content = T("Include selected linked models", "選択したリンク モデルを含める");

        _mappingsButton.Content = T("Edit category mappings...", "カテゴリ マッピングを編集...");
        _manageSchemaProfilesButton.Content = T("Schemas...", "スキーマ...");
        _manageValidationPoliciesButton.Content = T("Policies...", "ポリシー...");
        _browseButton.Content = T("Browse...", "参照...");
        _selectAllButton.Content = T("Select All", "すべて選択");
        _clearAllButton.Content = T("Clear All", "選択解除");
        _cancelButton.Content = T("Cancel", "キャンセル");
        _previewButton.Content = T("Preview...", "プレビュー...");
        _exportButton.Content = T("Export", "出力");
        _helpButton.Content = T("Help", "ヘルプ");
        _languageLabel.Text = T("Language", "言語");
        _versionText.Text = TF("Version {0}", "バージョン {0}", ProjectInfo.VersionTag);

        _unitCheckBox.ToolTip = T("Exports the `unit` layer.", "`unit` レイヤーを出力します。");
        _detailCheckBox.ToolTip = T("Exports the `detail` layer.", "`detail` レイヤーを出力します。");
        _openingCheckBox.ToolTip = T("Exports the `opening` layer.", "`opening` レイヤーを出力します。");
        _levelCheckBox.ToolTip = T("Exports the `level` layer.", "`level` レイヤーを出力します。");

        UpdateCoordinateInfoText(GetSelectedCoordinateMode() == CoordinateExportMode.ConvertToTargetCrs);
    }

    private void UpdateDisplayLanguages()
    {
        ViewSelectionRow.DisplayLanguage = _language;
        LanguageItem.DisplayLanguage = _language;
        CoordinateModeItem.DisplayLanguage = _language;
        UnitSourceItem.DisplayLanguage = _language;
        UnitAttributeSourceItem.DisplayLanguage = _language;
    }

    private void ConfigureFeatureCheckBox(CheckBox checkBox, string englishToolTip, string japaneseToolTip)
    {
        checkBox.Margin = new Thickness(0, 0, 0, 8);
        checkBox.Padding = new Thickness(2);
        checkBox.Checked += OnInputChanged;
        checkBox.Unchecked += OnInputChanged;
        checkBox.ToolTip = T(englishToolTip, japaneseToolTip);
    }

    private void ConfigureAdvancedOption(CheckBox checkBox)
    {
        checkBox.Padding = new Thickness(2);
        checkBox.Checked += OnInputChanged;
        checkBox.Unchecked += OnInputChanged;
    }

    private static FrameworkElement WrapStandaloneOption(CheckBox checkBox, Thickness margin)
    {
        return new Border
        {
            Margin = margin,
            Child = checkBox,
        };
    }

    private static void ConfigureInfoLine(TextBlock block, double bottomMargin = 4)
    {
        WpfDialogChrome.ConfigureInfoLine(block, bottomMargin);
    }

    private static Border CreateCard()
    {
        return WpfDialogChrome.CreateCard();
    }

    private Border CreateSectionCard(TextBlock title, TextBlock description, UIElement content)
    {
        return WpfDialogChrome.CreateSectionCard(title, description, content);
    }

    private FrameworkElement CreateFieldBlock(TextBlock label, UIElement control, double bottomMargin = 12)
    {
        return WpfDialogChrome.CreateFieldBlock(label, control, bottomMargin);
    }

    private static void StyleSectionTitle(TextBlock textBlock)
    {
        WpfDialogChrome.StyleSectionTitle(textBlock);
    }

    private static void StyleDescriptionText(TextBlock textBlock)
    {
        WpfDialogChrome.StyleDescriptionText(textBlock);
    }

    private static void StyleFieldLabel(TextBlock textBlock)
    {
        WpfDialogChrome.StyleFieldLabel(textBlock);
    }

    private static void StyleExpanderHeader(TextBlock textBlock)
    {
        WpfDialogChrome.StyleExpanderHeader(textBlock);
    }

    private string T(string english, string japanese)
    {
        return UiLanguageText.Select(_language, english, japanese);
    }

    private string TF(string englishFormat, string japaneseFormat, params object[] args)
    {
        return string.Format(T(englishFormat, japaneseFormat), args);
    }

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
                string levelName = View.GenLevel?.Name ?? UiLanguageText.Get(DisplayLanguage, "Common.NoLevel", "<no level>");
                string levelLabel = UiLanguageText.Get(DisplayLanguage, "Common.Level", "Level");
                return $"{View.Name} [{levelLabel}: {levelName}]";
            }
        }

        public bool IsSelected { get; set; }

        public override string ToString() => DisplayText;
    }

    private sealed class LinkSelectionRow
    {
        public LinkSelectionRow(LinkSelectionItem link)
        {
            Link = link ?? throw new ArgumentNullException(nameof(link));
        }

        public LinkSelectionItem Link { get; }

        public string DisplayText => Link.DisplayName;

        public bool IsSelected { get; set; }

        public override string ToString() => DisplayText;
    }

    private sealed class SchemaProfileItem
    {
        public SchemaProfileItem(SchemaProfile profile)
        {
            Profile = profile?.Clone() ?? throw new ArgumentNullException(nameof(profile));
        }

        public SchemaProfile Profile { get; }

        public override string ToString() => Profile.Name;
    }

    private sealed class ValidationPolicyProfileItem
    {
        public ValidationPolicyProfileItem(ValidationPolicyProfile profile)
        {
            Profile = profile?.Clone() ?? throw new ArgumentNullException(nameof(profile));
        }

        public ValidationPolicyProfile Profile { get; }

        public override string ToString() => Profile.Name;
    }

    private sealed class LanguageItem
    {
        public static UiLanguage DisplayLanguage { get; set; } = UiLanguage.English;

        public LanguageItem(UiLanguage language) => Language = language;

        public UiLanguage Language { get; }

        public override string ToString()
        {
            return UiLanguageText.DisplayName(Language);
        }
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
                ? UiLanguageText.Get(DisplayLanguage, "ExportDialog.CoordinateMode.ConvertToTargetCrs", "Convert to target CRS")
                : UiLanguageText.Get(DisplayLanguage, "ExportDialog.CoordinateMode.SharedCoordinatesDefault", "Shared coordinates (default)");
        }
    }

    private sealed class UnitSourceItem
    {
        public static UiLanguage DisplayLanguage { get; set; } = UiLanguage.English;

        public UnitSourceItem(UnitSource source) => Source = source;

        public UnitSource Source { get; }

        public override string ToString()
        {
            return Source == UnitSource.Rooms
                ? UiLanguageText.Get(DisplayLanguage, "Common.Rooms", "Rooms")
                : UiLanguageText.Get(DisplayLanguage, "Common.Floors", "Floors");
        }
    }

    private sealed class UnitAttributeSourceItem
    {
        public static UiLanguage DisplayLanguage { get; set; } = UiLanguage.English;

        public UnitAttributeSourceItem(UnitAttributeSource source) => Source = source;

        public UnitAttributeSource Source { get; }

        public override string ToString()
        {
            return Source switch
            {
                UnitAttributeSource.Rooms => UiLanguageText.Get(DisplayLanguage, "Common.Rooms", "Rooms"),
                UnitAttributeSource.Hybrid => UiLanguageText.Select(DisplayLanguage, "Hybrid (Rooms + Floor fallback)", "ハイブリッド (部屋 + 床フォールバック)"),
                _ => UiLanguageText.Get(DisplayLanguage, "Common.Floors", "Floors"),
            };
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

        public override string ToString() => $"EPSG:{Epsg} - {Name}";
    }
}





