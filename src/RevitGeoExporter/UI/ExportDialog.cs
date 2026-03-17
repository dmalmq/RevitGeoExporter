using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using RevitGeoExporter.Help;
using RevitGeoExporter.Core;
using RevitGeoExporter.Core.Models;
using RevitGeoExporter.Core.Preview;
using RevitGeoExporter.Core.Coordinates;
using RevitGeoExporter.Core.Geometry;
using RevitGeoExporter.Core.Schema;
using RevitGeoExporter.Core.Validation;
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
    private readonly ComboBox _incrementalModeComboBox = new();
    private readonly ComboBox _packagingModeComboBox = new();
    private readonly CheckBox _unitCheckBox = new();
    private readonly CheckBox _detailCheckBox = new();
    private readonly CheckBox _openingCheckBox = new();
    private readonly CheckBox _levelCheckBox = new();
    private readonly CheckBox _diagnosticsCheckBox = new();
    private readonly Label _unitSourceInlineLabel = new();
    private readonly ComboBox _unitSourceComboBox = new();
    private readonly Label _unitAttributeSourceInlineLabel = new();
    private readonly ComboBox _unitAttributeSourceComboBox = new();
    private readonly Label _roomParameterInlineLabel = new();
    private readonly TextBox _roomCategoryParameterTextBox = new();
    private readonly Label _linkedModelsLabel = new();
    private readonly CheckBox _includeLinkedModelsCheckBox = new();
    private readonly CheckedListBox _linkList = new();
    private readonly Label _schemaProfileInlineLabel = new();
    private readonly ComboBox _schemaProfileComboBox = new();
    private readonly Button _manageSchemaProfilesButton = new();
    private readonly Label _validationPolicyInlineLabel = new();
    private readonly ComboBox _validationPolicyComboBox = new();
    private readonly Button _manageValidationPoliciesButton = new();
    private readonly CheckBox _packageCheckBox = new();
    private readonly CheckBox _packageLegendCheckBox = new();
    private readonly CheckBox _validateAfterWriteCheckBox = new();
    private readonly CheckBox _generateQgisArtifactsCheckBox = new();
    private readonly CheckBox _openOutputFolderCheckBox = new();
    private readonly CheckBox _launchQgisCheckBox = new();
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
    private readonly Button _batchButton = new();
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
    private readonly Action<IWin32Window?>? _batchRequested;
    private readonly IReadOnlyList<LinkSelectionItem> _availableLinks;
    private readonly List<ExportProfile> _profiles;
    private readonly PreviewBasemapSettings _previewBasemapSettings;
    private readonly ModelCoordinateInfo? _coordinateInfo;
    private UiLanguage _language = UiLanguage.English;
    private bool _isApplyingProfile;
    private CoordinateExportMode _coordinateMode = CoordinateExportMode.SharedCoordinates;
    private UnitSource _unitSource = UnitSource.Floors;
    private UnitGeometrySource _unitGeometrySource = UnitGeometrySource.Unset;
    private UnitAttributeSource _unitAttributeSource = UnitAttributeSource.Unset;
    private string _roomCategoryParameterName = "Name";
    private LinkExportOptions _linkExportOptions = new();
    private List<SchemaProfile> _schemaProfiles = new() { SchemaProfile.CreateCoreProfile() };
    private string _activeSchemaProfileName = SchemaProfile.CoreProfileName;
    private List<ValidationPolicyProfile> _validationPolicyProfiles = ValidationPolicyProfile.NormalizeProfiles(null);
    private string _activeValidationPolicyProfileName = ValidationPolicyProfile.RecommendedProfileName;

    public ExportDialog(
        IReadOnlyList<ViewPlan> views,
        ExportDialogSettings settings,
        IReadOnlyList<LinkSelectionItem>? availableLinks = null,
        IReadOnlyList<ExportProfile>? profiles = null,
        Action<ExportProfileScope, string, ExportDialogSettings>? saveProfileRequested = null,
        Action<ExportProfile, string>? renameProfileRequested = null,
        Action<ExportProfile>? deleteProfileRequested = null,
        Action? openMappingsRequested = null,
        Action<IWin32Window?>? batchRequested = null,
        Action<ExportPreviewRequest>? previewRequested = null,
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

        _viewItems = views
            .Select(view => new ViewSelectionItem(view))
            .ToList();
        _availableLinks = (availableLinks ?? Array.Empty<LinkSelectionItem>()).ToList();
        _profiles = (profiles ?? Array.Empty<ExportProfile>()).ToList();
        _previewBasemapSettings = new PreviewBasemapSettings(settings.PreviewBasemapUrlTemplate, settings.PreviewBasemapAttribution);
        _coordinateInfo = coordinateInfo;
        _coordinateMode = settings.CoordinateMode;
        _linkExportOptions = settings.LinkExportOptions?.Clone() ?? new LinkExportOptions();
        _saveProfileRequested = saveProfileRequested;
        _renameProfileRequested = renameProfileRequested;
        _deleteProfileRequested = deleteProfileRequested;
        _openMappingsRequested = openMappingsRequested;
        _batchRequested = batchRequested;
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

        System.Windows.Forms.Panel scrollHost = new()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
        };

        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 17,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 228f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 104f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 178f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        scrollHost.Controls.Add(panel);
        _optionsGroup.Controls.Add(scrollHost);

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
                UnitSourceItem.DisplayLanguage = _language;
                UnitAttributeSourceItem.DisplayLanguage = _language;
                ProfileItem.DisplayLanguage = _language;
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
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        _unitCheckBox.AutoSize = true;
        _detailCheckBox.AutoSize = true;
        _openingCheckBox.AutoSize = true;
        _levelCheckBox.AutoSize = true;
        _unitCheckBox.Text = "unit";
        _detailCheckBox.Text = "detail";
        _openingCheckBox.Text = "opening";
        _levelCheckBox.Text = "level";
        _diagnosticsCheckBox.AutoSize = true;
        _packageCheckBox.AutoSize = true;
        _packageLegendCheckBox.AutoSize = true;
        _unitCheckBox.CheckedChanged += (_, _) =>
        {
            UpdateUnitOptionVisibility();
            UpdatePreviewButtonEnabled();
        };
        _detailCheckBox.CheckedChanged += (_, _) => UpdatePreviewButtonEnabled();
        _openingCheckBox.CheckedChanged += (_, _) => UpdatePreviewButtonEnabled();
        _levelCheckBox.CheckedChanged += (_, _) => UpdatePreviewButtonEnabled();
        _packageCheckBox.CheckedChanged += (_, _) => UpdatePackagingState();
        _unitSourceInlineLabel.AutoSize = true;
        _unitSourceComboBox.Width = 180;
        _unitSourceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _unitSourceComboBox.Items.Add(new UnitSourceItem(UnitSource.Floors));
        _unitSourceComboBox.Items.Add(new UnitSourceItem(UnitSource.Rooms));
        _unitSourceComboBox.SelectedIndexChanged += (_, _) =>
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
            UpdateUnitOptionVisibility();
        };

        _unitAttributeSourceInlineLabel.AutoSize = true;
        _unitAttributeSourceComboBox.Width = 220;
        _unitAttributeSourceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _unitAttributeSourceComboBox.Items.Add(new UnitAttributeSourceItem(UnitAttributeSource.Floors));
        _unitAttributeSourceComboBox.Items.Add(new UnitAttributeSourceItem(UnitAttributeSource.Rooms));
        _unitAttributeSourceComboBox.Items.Add(new UnitAttributeSourceItem(UnitAttributeSource.Hybrid));
        _unitAttributeSourceComboBox.SelectedIndexChanged += (_, _) =>
        {
            _unitAttributeSource = (_unitAttributeSourceComboBox.SelectedItem as UnitAttributeSourceItem)?.Source ?? UnitAttributeSource.Hybrid;
            SyncLegacyUnitSource();
            UpdateUnitOptionVisibility();
        };

        _roomParameterInlineLabel.AutoSize = true;
        _roomCategoryParameterTextBox.Width = 180;
        _roomCategoryParameterTextBox.TextChanged += (_, _) =>
        {
            string value = (_roomCategoryParameterTextBox.Text ?? string.Empty).Trim();
            _roomCategoryParameterName = value.Length == 0 ? "Name" : value;
        };
        _schemaProfileInlineLabel.AutoSize = true;
        _schemaProfileComboBox.Width = 220;
        _schemaProfileComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _schemaProfileComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (_schemaProfileComboBox.SelectedItem is SchemaProfileItem selected)
            {
                _activeSchemaProfileName = selected.Profile.Name;
            }
        };
        _manageSchemaProfilesButton.Width = 120;
        _manageSchemaProfilesButton.Height = 26;
        _manageSchemaProfilesButton.Click += (_, _) => EditSchemaProfiles();
        _validationPolicyInlineLabel.AutoSize = true;
        _validationPolicyComboBox.Width = 220;
        _validationPolicyComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _validationPolicyComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (_validationPolicyComboBox.SelectedItem is ValidationPolicyProfileItem selected)
            {
                _activeValidationPolicyProfileName = selected.Profile.Name;
            }
        };
        _manageValidationPoliciesButton.Width = 120;
        _manageValidationPoliciesButton.Height = 26;
        _manageValidationPoliciesButton.Click += (_, _) => EditValidationPolicies();

        featuresPanel.Controls.Add(_unitCheckBox);
        featuresPanel.Controls.Add(_detailCheckBox);
        featuresPanel.Controls.Add(_openingCheckBox);
        featuresPanel.Controls.Add(_levelCheckBox);
        featuresPanel.Controls.Add(_diagnosticsCheckBox);
        featuresPanel.Controls.Add(_packageCheckBox);
        featuresPanel.Controls.Add(_packageLegendCheckBox);
        featuresPanel.Controls.Add(_unitSourceInlineLabel);
        featuresPanel.Controls.Add(_unitSourceComboBox);
        featuresPanel.Controls.Add(_unitAttributeSourceInlineLabel);
        featuresPanel.Controls.Add(_unitAttributeSourceComboBox);
        featuresPanel.Controls.Add(_roomParameterInlineLabel);
        featuresPanel.Controls.Add(_roomCategoryParameterTextBox);
        featuresPanel.Controls.Add(_schemaProfileInlineLabel);
        featuresPanel.Controls.Add(_schemaProfileComboBox);
        featuresPanel.Controls.Add(_manageSchemaProfilesButton);
        featuresPanel.Controls.Add(_validationPolicyInlineLabel);
        featuresPanel.Controls.Add(_validationPolicyComboBox);
        featuresPanel.Controls.Add(_manageValidationPoliciesButton);
        panel.Controls.Add(featuresPanel, 0, 5);

        _linkedModelsLabel.Dock = DockStyle.Fill;
        _linkedModelsLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_linkedModelsLabel, 0, 6);

        TableLayoutPanel linkPanel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        linkPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        linkPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _includeLinkedModelsCheckBox.AutoSize = true;
        _includeLinkedModelsCheckBox.CheckedChanged += (_, _) =>
        {
            UpdateLinkSelectionState();
        };
        linkPanel.Controls.Add(_includeLinkedModelsCheckBox, 0, 0);

        _linkList.Dock = DockStyle.Fill;
        _linkList.CheckOnClick = true;
        _linkList.HorizontalScrollbar = true;
        _linkList.IntegralHeight = false;
        linkPanel.Controls.Add(_linkList, 0, 1);
        panel.Controls.Add(linkPanel, 0, 7);

        _outputDirectoryLabel.Dock = DockStyle.Fill;
        _outputDirectoryLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_outputDirectoryLabel, 0, 8);

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
        panel.Controls.Add(outputPanel, 0, 9);

        _crsLabel.Dock = DockStyle.Fill;
        _crsLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_crsLabel, 0, 10);

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
        panel.Controls.Add(crsPanel, 0, 11);

        Button mappingsButton = new()
        {
            Dock = DockStyle.Left,
            Width = 120,
            Text = "Mappings...",
        };
        mappingsButton.Click += (_, _) => _openMappingsRequested?.Invoke();
        panel.Controls.Add(mappingsButton, 0, 12);

        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Geometry Repair",
        }, 0, 13);
        panel.Controls.Add(BuildRepairPanel(), 0, 14);

        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Packaging",
        }, 0, 15);
        panel.Controls.Add(BuildPackagingPanel(), 0, 16);

        return _optionsGroup;
    }
    private WinFormsControl BuildPackagingPanel()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180f));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _incrementalModeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        if (_incrementalModeComboBox.Items.Count == 0)
        {
            _incrementalModeComboBox.Items.Add(new IncrementalModeItem(IncrementalExportMode.AllSelectedViews));
            _incrementalModeComboBox.Items.Add(new IncrementalModeItem(IncrementalExportMode.ChangedViewsOnly));
        }

        _packagingModeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        if (_packagingModeComboBox.Items.Count == 0)
        {
            _packagingModeComboBox.Items.Add(new PackagingModeItem(PackagingMode.PerViewPerFeatureFiles));
            _packagingModeComboBox.Items.Add(new PackagingModeItem(PackagingMode.PerViewGeoPackage));
            _packagingModeComboBox.Items.Add(new PackagingModeItem(PackagingMode.PerLevelGeoPackage));
            _packagingModeComboBox.Items.Add(new PackagingModeItem(PackagingMode.PerBuildingGeoPackage));
        }

        _validateAfterWriteCheckBox.AutoSize = true;
        _generateQgisArtifactsCheckBox.AutoSize = true;
        _openOutputFolderCheckBox.AutoSize = true;
        _launchQgisCheckBox.AutoSize = true;

        AddRepairRow(panel, 0, "Incremental mode", _incrementalModeComboBox);
        AddRepairRow(panel, 1, "Packaging mode", _packagingModeComboBox);
        AddRepairRow(panel, 2, "Validate after write", _validateAfterWriteCheckBox);
        AddRepairRow(panel, 3, "Generate QGIS artifacts", _generateQgisArtifactsCheckBox);
        AddRepairRow(panel, 4, "Open output folder", _openOutputFolderCheckBox);
        AddRepairRow(panel, 5, "Launch QGIS", _launchQgisCheckBox);
        return panel;
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

        _batchButton.Width = 90;
        _batchButton.Height = 30;
        _batchButton.Click += (_, _) => _batchRequested?.Invoke(this);

        _helpButton.Width = 90;
        _helpButton.Height = 30;
        _helpButton.Text = UiLanguageText.Get(_language, "Common.Help", "Help");
        _helpButton.Click += (_, _) => HelpLauncher.Show(this, HelpTopic.ExportWorkflow, _language, Text);

        actions.Controls.Add(_cancelButton);
        actions.Controls.Add(_exportButton);
        actions.Controls.Add(_previewButton);
        actions.Controls.Add(_batchButton);
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
        UnitSourceItem.DisplayLanguage = _language;
        UnitAttributeSourceItem.DisplayLanguage = _language;
        ProfileItem.DisplayLanguage = _language;

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
        _unitGeometrySource = UnitExportSettingsResolver.ResolveGeometrySource(settings.UnitSource, settings.UnitGeometrySource);
        _unitAttributeSource = UnitExportSettingsResolver.ResolveAttributeSource(settings.UnitSource, _unitGeometrySource, settings.UnitAttributeSource);
        _unitSource = UnitExportSettingsResolver.ToLegacy(_unitGeometrySource, _unitAttributeSource);
        _roomCategoryParameterName = string.IsNullOrWhiteSpace(settings.RoomCategoryParameterName) ? "Name" : settings.RoomCategoryParameterName.Trim();
        _linkExportOptions = settings.LinkExportOptions?.Clone() ?? new LinkExportOptions();
        _schemaProfiles = SchemaProfile.NormalizeProfiles(settings.SchemaProfiles).Select(profile => profile.Clone()).ToList();
        _activeSchemaProfileName = SchemaProfile.ResolveActiveName(_schemaProfiles, settings.ActiveSchemaProfileName);
        _validationPolicyProfiles = ValidationPolicyProfile.NormalizeProfiles(settings.ValidationPolicyProfiles).Select(profile => profile.Clone()).ToList();
        _activeValidationPolicyProfileName = ValidationPolicyProfile.ResolveActiveName(_validationPolicyProfiles, settings.ActiveValidationPolicyProfileName);
        SelectUnitSource(_unitSource);
        SelectUnitAttributeSource(_unitAttributeSource);
        _roomCategoryParameterTextBox.Text = _roomCategoryParameterName;
        PopulateSchemaProfiles();
        PopulateValidationPolicies();
        _diagnosticsCheckBox.Checked = settings.GenerateDiagnosticsReport;
        _packageCheckBox.Checked = settings.GeneratePackageOutput;
        _packageLegendCheckBox.Checked = settings.IncludePackageLegend;
        SelectIncrementalMode(settings.IncrementalExportMode);
        SelectPackagingMode(settings.PackagingMode);
        _validateAfterWriteCheckBox.Checked = settings.ValidateAfterWrite;
        _generateQgisArtifactsCheckBox.Checked = settings.GenerateQgisArtifacts;
        _openOutputFolderCheckBox.Checked = settings.PostExportActions?.OpenOutputFolder == true;
        _launchQgisCheckBox.Checked = settings.PostExportActions?.LaunchQgis == true;
        UpdatePackagingState();
        LoadGeometryRepairOptions(settings.GeometryRepairOptions);
        PopulateLinkList();

        _targetEpsgTextBox.Text = settings.TargetEpsg > 0
            ? settings.TargetEpsg.ToString()
            : ProjectInfo.DefaultTargetEpsg.ToString();

        SelectPresetIfAvailable(settings.TargetEpsg);
        SelectLanguage(_language);
        ApplyLanguage();
        UpdateProfileText();
        UpdateDiagnosticsText();
        UpdateVersionLabel();
        UpdateUnitOptionVisibility();
        UpdatePreviewButtonEnabled();
    }

    private void ApplyLanguage()
    {
        Text = UiLanguageText.Get(_language, "ExportDialog.Title", "Export GeoPackage");
        _viewsGroup.Text = UiLanguageText.Get(_language, "ExportDialog.PlanViews", "Plan Views");
        _optionsGroup.Text = UiLanguageText.Get(_language, "ExportDialog.Options", "Export Options");
        _languageLabel.Text = UiLanguageText.Get(_language, "Common.Language", "Language");
        _featureTypesLabel.Text = UiLanguageText.Get(_language, "ExportDialog.FeatureTypes", "Feature Types");
        _outputDirectoryLabel.Text = UiLanguageText.Get(_language, "Common.OutputDirectory", "Output Directory");
        _crsLabel.Text = UiLanguageText.Get(_language, "ExportDialog.CrsLabel", "CRS (EPSG)");
        _selectAllButton.Text = UiLanguageText.Get(_language, "ExportDialog.SelectAll", "Select All");
        _clearAllButton.Text = UiLanguageText.Get(_language, "ExportDialog.ClearAll", "Clear All");
        _browseButton.Text = UiLanguageText.Get(_language, "Common.Browse", "Browse...");
        _cancelButton.Text = UiLanguageText.Get(_language, "Common.Cancel", "Cancel");
        _previewButton.Text = UiLanguageText.Get(_language, "ExportDialog.Preview", "Preview...");
        _exportButton.Text = UiLanguageText.Get(_language, "ExportDialog.ExportButton", "Export");
        _batchButton.Text = UiLanguageText.Select(_language, "Batch...", "バッチ...");
        _helpButton.Text = UiLanguageText.Get(_language, "Common.Help", "Help");
        _packageCheckBox.Text = UiLanguageText.Get(_language, "ExportDialog.WritePackage", "Write GIS package");
        _packageLegendCheckBox.Text = UiLanguageText.Get(_language, "ExportDialog.IncludeLegend", "Include legend file");
        _validateAfterWriteCheckBox.Text = UiLanguageText.Select(_language, "Validate package outputs after write", "書き出し後にパッケージを検証");
        _generateQgisArtifactsCheckBox.Text = UiLanguageText.Select(_language, "Generate QGIS handoff files", "QGIS 引き継ぎファイルを生成");
        _openOutputFolderCheckBox.Text = UiLanguageText.Select(_language, "Open output folder after export", "出力後にフォルダーを開く");
        _launchQgisCheckBox.Text = UiLanguageText.Select(_language, "Launch QGIS after export", "出力後に QGIS を起動");
        _unitSourceInlineLabel.Text = UiLanguageText.Select(_language, "Unit Geometry Source", "ユニット形状の取得元");
        _unitAttributeSourceInlineLabel.Text = UiLanguageText.Select(_language, "Unit Attribute Source", "ユニット属性の取得元");
        _roomParameterInlineLabel.Text = UiLanguageText.Get(_language, "ExportDialog.RoomCategoryParameter", "Room Category Parameter");
        _schemaProfileInlineLabel.Text = UiLanguageText.Get(_language, "ExportDialog.SchemaProfile", "Schema Profile");
        _manageSchemaProfilesButton.Text = UiLanguageText.Get(_language, "ExportDialog.ManageSchemas", "Schemas...");
        _validationPolicyInlineLabel.Text = UiLanguageText.Select(_language, "Validation Policy", "検証ポリシー");
        _manageValidationPoliciesButton.Text = UiLanguageText.Select(_language, "Policies...", "ポリシー...");
        _linkedModelsLabel.Text = UiLanguageText.Get(_language, "ExportDialog.LinkedModels", "Linked Models");
        _includeLinkedModelsCheckBox.Text = UiLanguageText.Get(_language, "ExportDialog.IncludeLinkedModels", "Include selected linked models");
        _unitSourceComboBox.Refresh();
        _unitAttributeSourceComboBox.Refresh();
        _schemaProfileComboBox.Refresh();
        _validationPolicyComboBox.Refresh();
        if (_availableLinks.Count == 0)
        {
            PopulateLinkList();
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

    private void UpdateVersionLabel()
    {
        _versionLabel.Text = UiLanguageText.Format(_language, "Common.Version", "Version {0}", ProjectInfo.VersionTag);
    }

    private void UpdateDiagnosticsText()
    {
        _diagnosticsCheckBox.Text = UiLanguageText.Get(_language, "ExportDialog.WriteDiagnostics", "Write diagnostics report");
    }
    private void UpdateProfileText()
    {
        _profilesLabel.Text = UiLanguageText.Get(_language, "ExportDialog.ExportProfiles", "Export Profiles");
        _profileComboBox.Refresh();
    }

    private void ConfirmExport()
    {
        SyncLegacyUnitSource();
        List<ViewPlan> selectedViews = GetSelectedViews();
        if (selectedViews.Count == 0)
        {
            MessageBox.Show(
                this,
                UiLanguageText.Get(_language, "ExportDialog.Message.SelectPlanViewToExport", "Select at least one plan view to export."),
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
                UiLanguageText.Get(_language, "ExportDialog.Message.SelectFeatureType", "Select at least one feature type."),
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
                UiLanguageText.Get(_language, "ExportDialog.Message.ChooseOutputDirectory", "Choose an output directory."),
                ProjectInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!int.TryParse(_targetEpsgTextBox.Text, out int epsg) || epsg <= 0)
        {
            MessageBox.Show(
                this,
                UiLanguageText.Get(_language, "ExportDialog.Message.EnterValidEpsg", "Enter a valid EPSG code."),
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
            GetSelectedIncrementalMode(),
            _diagnosticsCheckBox.Checked,
            _packageCheckBox.Checked,
            _packageLegendCheckBox.Checked,
            GetSelectedPackagingMode(),
            _validateAfterWriteCheckBox.Checked,
            _generateQgisArtifactsCheckBox.Checked,
            BuildPostExportActions(),
            BuildGeometryRepairOptions(),
            (_profileComboBox.SelectedItem as ProfileItem)?.Profile?.Name,
            _language,
            _coordinateMode,
            _unitSource,
            _unitGeometrySource,
            _unitAttributeSource,
            _roomCategoryParameterName,
            BuildLinkExportOptions(),
            GetActiveSchemaProfile(),
            GetActiveValidationPolicyProfile());
        DialogResult = DialogResult.OK;
        Close();
    }
    private void ShowPreview()
    {
        if (_previewRequested == null)
        {
            return;
        }

        SyncLegacyUnitSource();
        List<ViewPlan> selectedViews = GetSelectedViews();
        if (selectedViews.Count == 0)
        {
            MessageBox.Show(
                this,
                UiLanguageText.Get(_language, "ExportDialog.Message.SelectPlanViewToPreview", "Select at least one plan view to preview."),
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
                UiLanguageText.Get(_language, "ExportDialog.Message.PreviewRequiresFeatureType", "Preview requires at least one selected feature type."),
                ProjectInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _previewRequested(new ExportPreviewRequest(
            selectedViews,
            previewTypes,
            BuildGeometryRepairOptions(),
            _language,
            _coordinateMode,
            ParseTargetEpsgOrDefault(),
            _coordinateInfo?.ResolvedSourceEpsg,
            _coordinateInfo?.SiteCoordinateSystemId,
            _coordinateInfo?.SiteCoordinateSystemDefinition,
            _coordinateInfo?.SurveyPointSharedCoordinates,
            _unitSource,
            _unitGeometrySource,
            _unitAttributeSource,
            _roomCategoryParameterName,
            BuildLinkExportOptions(),
            GetActiveSchemaProfile(),
            _previewBasemapSettings.UrlTemplate,
            _previewBasemapSettings.Attribution));
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

    private void PopulateLinkList()
    {
        _linkList.Items.Clear();
        if (_availableLinks.Count == 0)
        {
            _includeLinkedModelsCheckBox.Checked = false;
            _includeLinkedModelsCheckBox.Enabled = false;
            _linkList.Items.Add(UiLanguageText.Get(_language, "ExportDialog.NoLoadedLinks", "No loaded linked models found."));
            _linkList.Enabled = false;
            return;
        }

        _includeLinkedModelsCheckBox.Enabled = true;
        HashSet<long> selectedLinkIds = new(_linkExportOptions.SelectedLinkInstanceIds ?? new List<long>());
        foreach (LinkSelectionItem link in _availableLinks)
        {
            bool isSelected = _linkExportOptions.IncludeLinkedModels && selectedLinkIds.Contains(link.LinkInstanceId);
            _linkList.Items.Add(link, isSelected);
        }

        _includeLinkedModelsCheckBox.Checked = _linkExportOptions.IncludeLinkedModels;
        UpdateLinkSelectionState();
    }

    private void UpdateLinkSelectionState()
    {
        _linkList.Enabled = _availableLinks.Count > 0 && _includeLinkedModelsCheckBox.Checked;
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
        if (form.ShowDialog(this) != DialogResult.OK)
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
        if (form.ShowDialog(this) != DialogResult.OK)
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
        if (_availableLinks.Count == 0 || !_includeLinkedModelsCheckBox.Checked)
        {
            return new LinkExportOptions();
        }

        List<long> selectedLinkIds = new();
        foreach (object item in _linkList.CheckedItems)
        {
            if (item is LinkSelectionItem link)
            {
                selectedLinkIds.Add(link.LinkInstanceId);
            }
        }

        return new LinkExportOptions
        {
            IncludeLinkedModels = true,
            SelectedLinkInstanceIds = selectedLinkIds.Distinct().ToList(),
        };
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

    private int ParseTargetEpsgOrDefault()
    {
        return int.TryParse(_targetEpsgTextBox.Text, out int epsg) && epsg > 0
            ? epsg
            : ProjectInfo.DefaultTargetEpsg;
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

    private void SelectUnitSource(UnitSource source)
    {
        for (int i = 0; i < _unitSourceComboBox.Items.Count; i++)
        {
            if (_unitSourceComboBox.Items[i] is UnitSourceItem item && item.Source == source)
            {
                _unitSourceComboBox.SelectedIndex = i;
                return;
            }
        }

        _unitSourceComboBox.SelectedIndex = 0;
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

    private void UpdateUnitOptionVisibility()
    {
        bool unitsEnabled = _unitCheckBox.Checked;
        bool usesRoomAssignments = UnitExportSettingsResolver.UsesRoomCategoryAssignments(_unitAttributeSource);

        _unitSourceInlineLabel.Visible = unitsEnabled;
        _unitSourceComboBox.Visible = unitsEnabled;
        _unitAttributeSourceInlineLabel.Visible = unitsEnabled;
        _unitAttributeSourceComboBox.Visible = unitsEnabled;
        _roomParameterInlineLabel.Visible = unitsEnabled && usesRoomAssignments;
        _roomCategoryParameterTextBox.Visible = unitsEnabled && usesRoomAssignments;
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
        SyncLegacyUnitSource();
        int targetEpsg = int.TryParse(_targetEpsgTextBox.Text, out int epsg)
            ? epsg
            : ProjectInfo.DefaultTargetEpsg;

        return new ExportDialogSettings
        {
            OutputDirectory = (_outputDirectoryTextBox.Text ?? string.Empty).Trim(),
            TargetEpsg = targetEpsg,
            FeatureTypes = GetSelectedFeatureTypes(),
            SelectedViewIds = GetSelectedViews().Select(x => x.Id.Value).ToList(),
            IncrementalExportMode = GetSelectedIncrementalMode(),
            GenerateDiagnosticsReport = _diagnosticsCheckBox.Checked,
            GeneratePackageOutput = _packageCheckBox.Checked,
            IncludePackageLegend = _packageLegendCheckBox.Checked,
            PackagingMode = GetSelectedPackagingMode(),
            ValidateAfterWrite = _validateAfterWriteCheckBox.Checked,
            GenerateQgisArtifacts = _generateQgisArtifactsCheckBox.Checked,
            PostExportActions = BuildPostExportActions(),
            GeometryRepairOptions = BuildGeometryRepairOptions(),
            UiLanguage = _language,
            CoordinateMode = _coordinateMode,
            UnitSource = _unitSource,
            UnitGeometrySource = _unitGeometrySource,
            UnitAttributeSource = _unitAttributeSource,
            RoomCategoryParameterName = _roomCategoryParameterName,
            LinkExportOptions = BuildLinkExportOptions(),
            SchemaProfiles = _schemaProfiles.Select(profile => profile.Clone()).ToList(),
            ActiveSchemaProfileName = _activeSchemaProfileName,
            ValidationPolicyProfiles = _validationPolicyProfiles.Select(profile => profile.Clone()).ToList(),
            ActiveValidationPolicyProfileName = _activeValidationPolicyProfileName,
            PreviewBasemapUrlTemplate = _previewBasemapSettings.UrlTemplate,
            PreviewBasemapAttribution = _previewBasemapSettings.Attribution,
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
            _coordinateMode = settings.CoordinateMode;
            _unitCheckBox.Checked = settings.FeatureTypes.HasFlag(ExportFeatureType.Unit);
            _detailCheckBox.Checked = settings.FeatureTypes.HasFlag(ExportFeatureType.Detail);
            _openingCheckBox.Checked = settings.FeatureTypes.HasFlag(ExportFeatureType.Opening);
            _levelCheckBox.Checked = settings.FeatureTypes.HasFlag(ExportFeatureType.Level);
            _unitGeometrySource = UnitExportSettingsResolver.ResolveGeometrySource(settings.UnitSource, settings.UnitGeometrySource);
            _unitAttributeSource = UnitExportSettingsResolver.ResolveAttributeSource(settings.UnitSource, _unitGeometrySource, settings.UnitAttributeSource);
            _unitSource = UnitExportSettingsResolver.ToLegacy(_unitGeometrySource, _unitAttributeSource);
            _roomCategoryParameterName = string.IsNullOrWhiteSpace(settings.RoomCategoryParameterName) ? "Name" : settings.RoomCategoryParameterName.Trim();
            _linkExportOptions = settings.LinkExportOptions?.Clone() ?? new LinkExportOptions();
            _schemaProfiles = SchemaProfile.NormalizeProfiles(settings.SchemaProfiles).Select(profile => profile.Clone()).ToList();
            _activeSchemaProfileName = SchemaProfile.ResolveActiveName(_schemaProfiles, settings.ActiveSchemaProfileName);
            _validationPolicyProfiles = ValidationPolicyProfile.NormalizeProfiles(settings.ValidationPolicyProfiles).Select(profile => profile.Clone()).ToList();
            _activeValidationPolicyProfileName = ValidationPolicyProfile.ResolveActiveName(_validationPolicyProfiles, settings.ActiveValidationPolicyProfileName);
            SelectUnitSource(_unitSource);
            SelectUnitAttributeSource(_unitAttributeSource);
            _roomCategoryParameterTextBox.Text = _roomCategoryParameterName;
            PopulateSchemaProfiles();
            PopulateValidationPolicies();
            _diagnosticsCheckBox.Checked = settings.GenerateDiagnosticsReport;
            _packageCheckBox.Checked = settings.GeneratePackageOutput;
            _packageLegendCheckBox.Checked = settings.IncludePackageLegend;
            _packageLegendCheckBox.Enabled = _packageCheckBox.Checked;
            LoadGeometryRepairOptions(settings.GeometryRepairOptions);
            SelectPresetIfAvailable(settings.TargetEpsg);
            SelectLanguage(settings.UiLanguage);
            PopulateLinkList();
            UpdateUnitOptionVisibility();
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
            OpeningSnapDistanceMeters = ParseDouble(_openingSnapDistanceTextBox.Text, 0.20d),
            ElevatorOpeningSnapDistanceMeters = ParseDouble(_elevatorSnapDistanceTextBox.Text, 0.20d),
            MergeNearbyBoundaryThresholdMeters = ParseDouble(_mergeBoundaryThresholdTextBox.Text, 0.15d),
        };
    }

    private static double ParseDouble(string? value, double fallback)
    {
        return double.TryParse(value, out double parsed) ? parsed : fallback;
    }

    private void UpdatePackagingState()
    {
        bool packageEnabled = _packageCheckBox.Checked;
        _packageLegendCheckBox.Enabled = packageEnabled;
        _generateQgisArtifactsCheckBox.Enabled = packageEnabled;
        _launchQgisCheckBox.Enabled = packageEnabled;
    }

    private IncrementalExportMode GetSelectedIncrementalMode()
    {
        return (_incrementalModeComboBox.SelectedItem as IncrementalModeItem)?.Mode ?? IncrementalExportMode.AllSelectedViews;
    }

    private PackagingMode GetSelectedPackagingMode()
    {
        return (_packagingModeComboBox.SelectedItem as PackagingModeItem)?.Mode ?? PackagingMode.PerViewPerFeatureFiles;
    }

    private void SelectIncrementalMode(IncrementalExportMode mode)
    {
        for (int i = 0; i < _incrementalModeComboBox.Items.Count; i++)
        {
            if (_incrementalModeComboBox.Items[i] is IncrementalModeItem item && item.Mode == mode)
            {
                _incrementalModeComboBox.SelectedIndex = i;
                return;
            }
        }

        _incrementalModeComboBox.SelectedIndex = 0;
    }

    private void SelectPackagingMode(PackagingMode mode)
    {
        for (int i = 0; i < _packagingModeComboBox.Items.Count; i++)
        {
            if (_packagingModeComboBox.Items[i] is PackagingModeItem item && item.Mode == mode)
            {
                _packagingModeComboBox.SelectedIndex = i;
                return;
            }
        }

        _packagingModeComboBox.SelectedIndex = 0;
    }

    private PostExportActionOptions BuildPostExportActions()
    {
        return new PostExportActionOptions
        {
            OpenOutputFolder = _openOutputFolderCheckBox.Checked,
            LaunchQgis = _launchQgisCheckBox.Checked,
        };
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
            string levelName = View.GenLevel?.Name ?? UiLanguageText.Get(DisplayLanguage, "Common.NoLevel", "<no level>");
            string levelLabel = UiLanguageText.Get(DisplayLanguage, "Common.Level", "Level");
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

    private sealed class IncrementalModeItem
    {
        public IncrementalModeItem(IncrementalExportMode mode)
        {
            Mode = mode;
        }

        public IncrementalExportMode Mode { get; }

        public override string ToString()
        {
            return Mode == IncrementalExportMode.ChangedViewsOnly
                ? "Changed views only"
                : "All selected views";
        }
    }

    private sealed class PackagingModeItem
    {
        public PackagingModeItem(PackagingMode mode)
        {
            Mode = mode;
        }

        public PackagingMode Mode { get; }

        public override string ToString()
        {
            return Mode switch
            {
                PackagingMode.PerViewGeoPackage => "Per view GeoPackage",
                PackagingMode.PerLevelGeoPackage => "Per level GeoPackage",
                PackagingMode.PerBuildingGeoPackage => "Per building GeoPackage",
                _ => "Per view / feature files",
            };
        }
    }


    private sealed class UnitSourceItem
    {
        public static UiLanguage DisplayLanguage { get; set; } = UiLanguage.English;

        public UnitSourceItem(UnitSource source)
        {
            Source = source;
        }

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

        public UnitAttributeSourceItem(UnitAttributeSource source)
        {
            Source = source;
        }

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
                return UiLanguageText.Get(DisplayLanguage, "ExportDialog.CurrentSettings", "(Current settings)");
            }

            string scopeLabel = Profile.Scope == ExportProfileScope.Project
                ? UiLanguageText.Get(DisplayLanguage, "SettingsHub.ProjectTab", "Project")
                : UiLanguageText.Get(DisplayLanguage, "SettingsHub.GlobalTab", "Global");
            return $"[{scopeLabel}] {Profile.Name}";
        }
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
}


