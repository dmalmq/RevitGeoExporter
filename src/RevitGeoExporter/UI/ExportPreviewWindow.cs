using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Forms.Integration;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using RevitGeoExporter.Help;
using RevitGeoExporter.Export;
using Color = System.Windows.Media.Color;
using Grid = System.Windows.Controls.Grid;
using Visibility = System.Windows.Visibility;
using WinForms = System.Windows.Forms;
using WpfGrid = System.Windows.Controls.Grid;

namespace RevitGeoExporter.UI;

internal sealed class ExportPreviewWindow : IDisposable
{
    private readonly ExportPreviewController _controller;
    private readonly Window _window;
    private readonly PreviewCanvasControl _canvas = new();
    private readonly ComboBox _viewComboBox = new();
    private readonly TextBox _searchTextBox = new();
    private readonly CheckBox _unitsCheckBox = new();
    private readonly CheckBox _openingsCheckBox = new();
    private readonly CheckBox _detailsCheckBox = new();
    private readonly CheckBox _levelsCheckBox = new();
    private readonly CheckBox _stairsCheckBox = new();
    private readonly CheckBox _escalatorsCheckBox = new();
    private readonly CheckBox _elevatorsCheckBox = new();
    private readonly CheckBox _warningsOnlyCheckBox = new();
    private readonly CheckBox _overriddenOnlyCheckBox = new();
    private readonly CheckBox _unassignedOnlyCheckBox = new();
    private readonly CheckBox _basemapCheckBox = new();
    private readonly CheckBox _surveyPointCheckBox = new();
    private readonly TextBlock _basemapHintText = new();
    private readonly TextBlock _surveyPointHintText = new();
    private readonly TextBlock _viewSummaryText = new();
    private readonly TextBlock _viewCoordinateText = new();
    private readonly TextBlock _viewHelperText = new();
    private readonly StackPanel _legendPanel = new();
    private readonly TextBlock _legendEmptyText = new();
    private readonly ScrollViewer _legendScrollViewer = new();
    private readonly Button _legendCollapseButton = new();
    private readonly TextBlock _warningsSummaryText = new();
    private readonly ListBox _warningsListBox = new();
    private readonly TextBlock _warningsEmptyText = new();
    private readonly StackPanel _detailsPanel = new();
    private readonly TextBlock _detailsPlaceholderText = new();
    private readonly ListBox _unassignedFloorsListBox = new();
    private readonly TextBlock _unassignedEmptyText = new();
    private readonly Border _assignmentPendingBadge = new();
    private readonly TextBlock _assignmentPendingText = new();
    private readonly TextBlock _assignmentSourceText = new();
    private readonly TextBlock _assignmentTargetValueText = new();
    private readonly TextBlock _assignmentCandidateValueText = new();
    private readonly TextBlock _assignmentCurrentValueText = new();
    private readonly ComboBox _assignmentCategoryComboBox = new();
    private readonly Button _assignButton = new();
    private readonly Button _clearAssignmentButton = new();
    private readonly Button _saveAssignmentsButton = new();
    private readonly Button _discardAssignmentsButton = new();
    private readonly TextBlock _assignmentHintText = new();
    private readonly TextBlock _footerSummaryText = new();
    private readonly Button _fitButton = new();
    private readonly Button _resetButton = new();
    private readonly Button _helpButton = new();
    private readonly Button _closeButton = new();

    private bool _isLoadingView;
    private bool _suppressUnassignedSelectionChanged;
    private GridLength _sidebarExpandedWidth = new(250);
    private GridLength _inspectorExpandedWidth = new(360);

    public ExportPreviewWindow(
        ExportPreviewRequest request,
        ExportPreviewService previewService,
        WinForms.IWin32Window? owner = null)
    {
        _controller = new ExportPreviewController(request, previewService);
        _canvas.BasemapStatusChanged += OnBasemapStatusChanged;
        _canvas.SelectedFeatureChanged += OnSelectedFeatureChanged;

        _window = new Window
        {
            Title = T("Export Preview", "エクスポート プレビュー"),
            Width = 1420,
            Height = 900,
            MinWidth = 1160,
            MinHeight = 760,
            Background = WpfDialogChrome.WindowBackgroundBrush,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Content = BuildLayout(),
        };
        _window.Closing += OnWindowClosing;

        if (owner != null && owner.Handle != IntPtr.Zero)
        {
            new WindowInteropHelper(_window).Owner = owner.Handle;
        }

        LoadRequest(request);
    }

    public bool? ShowDialog()
    {
        return _window.ShowDialog();
    }

    public void Dispose()
    {
        if (_window.IsVisible)
        {
            _window.Close();
        }

        _canvas.Dispose();
    }

    private UIElement BuildLayout()
    {
        WpfGrid root = new()
        {
            Margin = new Thickness(16),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        WpfGrid body = new();
        ColumnDefinition sidebarColumn = new() { Width = new GridLength(250), MinWidth = 180 };
        ColumnDefinition leftSplitterColumn = new() { Width = GridLength.Auto };
        ColumnDefinition mapColumn = new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 };
        ColumnDefinition rightSplitterColumn = new() { Width = GridLength.Auto };
        ColumnDefinition inspectorColumn = new() { Width = new GridLength(360), MinWidth = 280 };
        body.ColumnDefinitions.Add(sidebarColumn);
        body.ColumnDefinitions.Add(leftSplitterColumn);
        body.ColumnDefinitions.Add(mapColumn);
        body.ColumnDefinitions.Add(rightSplitterColumn);
        body.ColumnDefinitions.Add(inspectorColumn);

        // Sidebar with collapse button
        UIElement sidebarContent = BuildSidebar();
        GridSplitter leftSplitter = CreateGridSplitter();
        WpfGrid.SetColumn(leftSplitter, 1);

        UIElement sidebarPanel = BuildCollapsiblePanel(
            sidebarContent, "\u25C0", "\u25B6", isLeftSide: true,
            onToggle: (collapsed) =>
            {
                if (collapsed)
                {
                    _sidebarExpandedWidth = sidebarColumn.Width;
                    sidebarColumn.Width = GridLength.Auto;
                    sidebarColumn.MinWidth = 0;
                    leftSplitter.Visibility = Visibility.Collapsed;
                }
                else
                {
                    sidebarColumn.Width = _sidebarExpandedWidth;
                    sidebarColumn.MinWidth = 180;
                    leftSplitter.Visibility = Visibility.Visible;
                }
            });
        body.Children.Add(sidebarPanel);
        body.Children.Add(leftSplitter);

        // Map workspace
        UIElement workspace = BuildWorkspace();
        WpfGrid.SetColumn(workspace, 2);
        if (workspace is FrameworkElement workspaceElement)
        {
            workspaceElement.Margin = new Thickness(6, 0, 6, 0);
        }

        body.Children.Add(workspace);

        // Inspector with collapse button
        GridSplitter rightSplitter = CreateGridSplitter();
        WpfGrid.SetColumn(rightSplitter, 3);

        UIElement inspectorContent = BuildInspectorCard();
        UIElement inspectorPanel = BuildCollapsiblePanel(
            inspectorContent, "\u25B6", "\u25C0", isLeftSide: false,
            onToggle: (collapsed) =>
            {
                if (collapsed)
                {
                    _inspectorExpandedWidth = inspectorColumn.Width;
                    inspectorColumn.Width = GridLength.Auto;
                    inspectorColumn.MinWidth = 0;
                    rightSplitter.Visibility = Visibility.Collapsed;
                }
                else
                {
                    inspectorColumn.Width = _inspectorExpandedWidth;
                    inspectorColumn.MinWidth = 280;
                    rightSplitter.Visibility = Visibility.Visible;
                }
            });
        WpfGrid.SetColumn(inspectorPanel, 4);
        body.Children.Add(rightSplitter);
        body.Children.Add(inspectorPanel);

        root.Children.Add(body);

        UIElement footer = BuildFooter();
        WpfGrid.SetRow(footer, 1);
        root.Children.Add(footer);

        return root;
    }

    private static GridSplitter CreateGridSplitter()
    {
        return new GridSplitter
        {
            Width = 5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeWE,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        };
    }

    private static UIElement BuildCollapsiblePanel(
        UIElement content,
        string collapseGlyph,
        string expandGlyph,
        bool isLeftSide,
        Action<bool> onToggle)
    {
        WpfGrid container = new();
        if (isLeftSide)
        {
            container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            container.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        else
        {
            container.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        int contentColumn = isLeftSide ? 0 : 1;
        int buttonColumn = isLeftSide ? 1 : 0;

        if (content is FrameworkElement fe)
        {
            WpfGrid.SetColumn(fe, contentColumn);
        }

        container.Children.Add(content);

        Button toggleButton = new()
        {
            Content = collapseGlyph,
            Width = 20,
            FontSize = 10,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            ToolTip = "Collapse",
        };
        WpfGrid.SetColumn(toggleButton, buttonColumn);

        bool isCollapsed = false;
        toggleButton.Click += (_, _) =>
        {
            isCollapsed = !isCollapsed;
            if (content is FrameworkElement element)
            {
                element.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
            }

            toggleButton.Content = isCollapsed ? expandGlyph : collapseGlyph;
            toggleButton.ToolTip = isCollapsed ? "Expand" : "Collapse";
            onToggle(isCollapsed);
        };

        container.Children.Add(toggleButton);
        return container;
    }

    private UIElement BuildSidebar()
    {
        StackPanel sidebar = new();
        sidebar.Children.Add(BuildSearchCard());
        sidebar.Children.Add(BuildMapLayersCard());
        sidebar.Children.Add(BuildFiltersCard());
        sidebar.Children.Add(BuildViewToolsCard());

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = sidebar,
        };
    }

    private UIElement BuildSearchCard()
    {
        TextBlock title = new();
        TextBlock description = new();
        StackPanel content = new();

        _searchTextBox.MinHeight = 32;
        _searchTextBox.TextChanged += (_, _) => ApplyCanvasFilters();

        TextBlock label = new();
        content.Children.Add(WpfDialogChrome.CreateFieldBlock(label, _searchTextBox, 0));

        ApplySearchLanguage(title, description, label);
        return WpfDialogChrome.CreateSectionCard(title, description, content);
    }

    private UIElement BuildMapLayersCard()
    {
        TextBlock title = new();
        TextBlock description = new();
        StackPanel content = new();

        ConfigureLayerCheckBox(_unitsCheckBox, T("Units", "ユニット"));
        ConfigureLayerCheckBox(_openingsCheckBox, T("Openings", "開口"));
        ConfigureLayerCheckBox(_detailsCheckBox, T("Details", "ディテール"));
        ConfigureLayerCheckBox(_levelsCheckBox, T("Levels", "レベル"));
        ConfigureLayerCheckBox(_stairsCheckBox, T("Stairs", "階段"));
        ConfigureLayerCheckBox(_escalatorsCheckBox, T("Escalators", "エスカレーター"));
        ConfigureLayerCheckBox(_elevatorsCheckBox, T("Elevators", "エレベーター"));
        ConfigureLayerCheckBox(_basemapCheckBox, L("Preview.ShowBasemap", "Show basemap"));
        ConfigureLayerCheckBox(_surveyPointCheckBox, L("Preview.ShowSurveyPoint", "Show survey point"));

        _stairsCheckBox.IsChecked = true;
        _escalatorsCheckBox.IsChecked = true;
        _elevatorsCheckBox.IsChecked = true;

        _surveyPointCheckBox.Checked += (_, _) =>
        {
            ApplyCanvasFilters();
            if (_surveyPointCheckBox.IsEnabled && _surveyPointCheckBox.IsChecked == true)
            {
                _canvas.FitToFeatures();
            }
        };

        content.Children.Add(_unitsCheckBox);
        content.Children.Add(_openingsCheckBox);
        content.Children.Add(_detailsCheckBox);
        content.Children.Add(_levelsCheckBox);
        content.Children.Add(_stairsCheckBox);
        content.Children.Add(_escalatorsCheckBox);
        content.Children.Add(_elevatorsCheckBox);
        content.Children.Add(_basemapCheckBox);
        content.Children.Add(CreateHintText(_basemapHintText, new Thickness(24, -2, 0, 8)));
        content.Children.Add(_surveyPointCheckBox);
        content.Children.Add(CreateHintText(_surveyPointHintText, new Thickness(24, -2, 0, 0)));

        ApplyMapLayersLanguage(title, description);
        return WpfDialogChrome.CreateSectionCard(title, description, content);
    }

    private UIElement BuildFiltersCard()
    {
        TextBlock title = new();
        TextBlock description = new();
        StackPanel content = new();

        ConfigureLayerCheckBox(_warningsOnlyCheckBox, T("Warnings only", "警告のみ"));
        ConfigureLayerCheckBox(_overriddenOnlyCheckBox, T("Overrides only", "上書きのみ"));
        ConfigureLayerCheckBox(_unassignedOnlyCheckBox, T("Unassigned only", "未割り当てのみ"));

        content.Children.Add(_warningsOnlyCheckBox);
        content.Children.Add(_overriddenOnlyCheckBox);
        content.Children.Add(_unassignedOnlyCheckBox);

        ApplyFiltersLanguage(title, description);
        return WpfDialogChrome.CreateSectionCard(title, description, content);
    }

    private UIElement BuildViewToolsCard()
    {
        TextBlock title = new();
        TextBlock description = new();
        StackPanel content = new();

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
        };

        _fitButton.MinWidth = 88;
        _fitButton.Padding = new Thickness(12, 6, 12, 6);
        _fitButton.Click += (_, _) => _canvas.FitToFeatures();
        actions.Children.Add(_fitButton);

        _resetButton.MinWidth = 88;
        _resetButton.Padding = new Thickness(12, 6, 12, 6);
        _resetButton.Margin = new Thickness(8, 0, 0, 0);
        _resetButton.Click += (_, _) => _canvas.ResetView();
        actions.Children.Add(_resetButton);

        content.Children.Add(actions);

        ApplyViewToolsLanguage(title, description);
        return WpfDialogChrome.CreateSectionCard(title, description, content);
    }

    private UIElement BuildWorkspace()
    {
        Border mapCard = WpfDialogChrome.CreateCard(new Thickness(0));
        mapCard.Child = new WindowsFormsHost
        {
            Child = _canvas,
        };

        return mapCard;
    }

    private UIElement BuildInspectorCard()
    {
        Border card = WpfDialogChrome.CreateCard();

        StackPanel layout = new();

        // --- View selector (moved from header) ---
        TextBlock viewTitle = new();
        WpfDialogChrome.StyleSectionTitle(viewTitle);
        viewTitle.Text = T("Preview view", "プレビュー ビュー");
        layout.Children.Add(viewTitle);

        TextBlock viewDescription = new();
        WpfDialogChrome.StyleDescriptionText(viewDescription);
        viewDescription.Text = T(
            "Switch between the selected plan views and inspect the export output before writing files.",
            "選択した平面ビューを切り替えながら、出力前の内容を確認します。");
        layout.Children.Add(viewDescription);

        _viewComboBox.MinHeight = 34;
        _viewComboBox.SelectionChanged += (_, _) => LoadSelectedView(fitViewport: true);
        TextBlock viewLabel = new() { Text = T("View", "ビュー") };
        layout.Children.Add(WpfDialogChrome.CreateFieldBlock(viewLabel, _viewComboBox, 10));

        Border summaryCard = WpfDialogChrome.CreateCard(new Thickness(12));
        summaryCard.Background = WpfDialogChrome.StatusBackgroundBrush;
        summaryCard.BorderBrush = WpfDialogChrome.CardBorderBrush;

        StackPanel summaryStack = new();
        _viewSummaryText.FontWeight = FontWeights.SemiBold;
        _viewSummaryText.Foreground = WpfDialogChrome.StatusTextBrush;
        _viewSummaryText.TextWrapping = TextWrapping.Wrap;
        summaryStack.Children.Add(_viewSummaryText);

        _viewCoordinateText.Margin = new Thickness(0, 6, 0, 0);
        _viewCoordinateText.Foreground = WpfDialogChrome.MutedTextBrush;
        _viewCoordinateText.TextWrapping = TextWrapping.Wrap;
        summaryStack.Children.Add(_viewCoordinateText);

        _viewHelperText.Margin = new Thickness(0, 8, 0, 0);
        _viewHelperText.Foreground = WpfDialogChrome.MutedTextBrush;
        _viewHelperText.TextWrapping = TextWrapping.Wrap;
        summaryStack.Children.Add(_viewHelperText);

        summaryCard.Child = summaryStack;
        layout.Children.Add(summaryCard);

        // --- Collapsible legend ---
        layout.Children.Add(BuildLegendSection());

        // --- Separator ---
        layout.Children.Add(new Border
        {
            BorderBrush = WpfDialogChrome.CardBorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 14, 0, 14),
        });

        // --- Inspector ---
        TextBlock title = new();
        TextBlock description = new();
        WpfDialogChrome.StyleSectionTitle(title);
        WpfDialogChrome.StyleDescriptionText(description);
        layout.Children.Add(title);
        layout.Children.Add(description);

        TabControl tabs = new()
        {
            Margin = new Thickness(0, 12, 0, 0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
        };

        tabs.Items.Add(new TabItem
        {
            Header = T("Details", "詳細"),
            Content = BuildDetailsTab(),
        });
        tabs.Items.Add(new TabItem
        {
            Header = T("Assignments", "割り当て"),
            Content = BuildAssignmentsTab(),
        });
        tabs.Items.Add(new TabItem
        {
            Header = T("Warnings", "警告"),
            Content = BuildWarningsTab(),
        });

        layout.Children.Add(tabs);

        title.Text = T("Inspector", "インスペクター");
        description.Text = T(
            "Inspect metadata, staged assignments, and warnings for the current preview.",
            "現在のプレビューに対するメタデータ、保留中の割り当て、警告を確認します。");

        card.Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = layout,
        };
        return card;
    }

    private UIElement BuildLegendSection()
    {
        StackPanel section = new()
        {
            Margin = new Thickness(0, 12, 0, 0),
        };

        // Header row with title and collapse toggle
        DockPanel header = new() { LastChildFill = false };

        TextBlock legendTitle = new()
        {
            Text = T("Legend", "凡例"),
            FontWeight = FontWeights.SemiBold,
            Foreground = WpfDialogChrome.StatusTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(legendTitle, Dock.Left);
        header.Children.Add(legendTitle);

        _legendCollapseButton.Content = "\u25BC";
        _legendCollapseButton.FontSize = 10;
        _legendCollapseButton.Padding = new Thickness(6, 2, 6, 2);
        _legendCollapseButton.Background = Brushes.Transparent;
        _legendCollapseButton.BorderThickness = new Thickness(0);
        _legendCollapseButton.Cursor = System.Windows.Input.Cursors.Hand;
        _legendCollapseButton.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_legendCollapseButton, Dock.Right);
        _legendCollapseButton.Click += (_, _) =>
        {
            if (_legendScrollViewer.Visibility == Visibility.Visible)
            {
                _legendScrollViewer.Visibility = Visibility.Collapsed;
                _legendCollapseButton.Content = "\u25B6";
            }
            else
            {
                _legendScrollViewer.Visibility = Visibility.Visible;
                _legendCollapseButton.Content = "\u25BC";
            }
        };
        header.Children.Add(_legendCollapseButton);

        section.Children.Add(header);

        // Legend entries in a scrollable vertical list
        _legendPanel.Orientation = Orientation.Vertical;
        _legendScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _legendScrollViewer.MaxHeight = 180;
        _legendScrollViewer.Margin = new Thickness(0, 6, 0, 0);
        _legendScrollViewer.Content = _legendPanel;
        section.Children.Add(_legendScrollViewer);

        _legendEmptyText.Foreground = WpfDialogChrome.MutedTextBrush;
        _legendEmptyText.TextWrapping = TextWrapping.Wrap;
        _legendEmptyText.Margin = new Thickness(0, 6, 0, 0);
        _legendEmptyText.Visibility = Visibility.Collapsed;
        section.Children.Add(_legendEmptyText);

        return section;
    }

    private UIElement BuildDetailsTab()
    {
        StackPanel content = new()
        {
            Margin = new Thickness(0, 12, 0, 0),
        };

        _detailsPlaceholderText.Foreground = WpfDialogChrome.MutedTextBrush;
        _detailsPlaceholderText.TextWrapping = TextWrapping.Wrap;
        _detailsPlaceholderText.Margin = new Thickness(0, 0, 0, 12);
        content.Children.Add(_detailsPlaceholderText);
        content.Children.Add(_detailsPanel);

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content,
        };
    }

    private UIElement BuildAssignmentsTab()
    {
        StackPanel content = new()
        {
            Margin = new Thickness(0, 12, 0, 0),
        };

        content.Children.Add(new TextBlock
        {
            Text = T(
                "Assign project-specific categories to unresolved floor-derived or room-derived units.",
                "未解決の床由来または部屋由来ユニットに、プロジェクト固有のカテゴリを割り当てます。"),
            TextWrapping = TextWrapping.Wrap,
        });

        _assignmentPendingBadge.Padding = new Thickness(10, 6, 10, 6);
        _assignmentPendingBadge.CornerRadius = new CornerRadius(6);
        _assignmentPendingBadge.Margin = new Thickness(0, 12, 0, 0);
        _assignmentPendingBadge.Child = _assignmentPendingText;
        content.Children.Add(_assignmentPendingBadge);

        _assignmentSourceText.Margin = new Thickness(0, 10, 0, 0);
        _assignmentSourceText.Foreground = WpfDialogChrome.MutedTextBrush;
        _assignmentSourceText.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(_assignmentSourceText);

        content.Children.Add(new TextBlock
        {
            Text = T("Unassigned floor types", "未割り当ての床タイプ"),
            Margin = new Thickness(0, 14, 0, 6),
            FontWeight = FontWeights.SemiBold,
            Foreground = WpfDialogChrome.StatusTextBrush,
        });

        Border listBorder = new()
        {
            BorderBrush = WpfDialogChrome.CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.White,
            Height = 180,
        };

        _unassignedFloorsListBox.BorderThickness = new Thickness(0);
        _unassignedFloorsListBox.Background = Brushes.Transparent;
        _unassignedFloorsListBox.SelectionMode = SelectionMode.Extended;
        _unassignedFloorsListBox.SelectionChanged += (_, _) => OnUnassignedFloorSelectionChanged();
        listBorder.Child = _unassignedFloorsListBox;
        content.Children.Add(listBorder);

        _unassignedEmptyText.Margin = new Thickness(0, 8, 0, 0);
        _unassignedEmptyText.Foreground = WpfDialogChrome.MutedTextBrush;
        _unassignedEmptyText.TextWrapping = TextWrapping.Wrap;
        _unassignedEmptyText.Visibility = Visibility.Collapsed;
        content.Children.Add(_unassignedEmptyText);

        Border targetCard = WpfDialogChrome.CreateCard(new Thickness(12));
        targetCard.Margin = new Thickness(0, 14, 0, 0);
        targetCard.Child = BuildAssignmentSummaryGrid();
        content.Children.Add(targetCard);

        StackPanel categoryActions = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 14, 0, 0),
        };
        _assignmentCategoryComboBox.MinWidth = 190;
        _assignmentCategoryComboBox.MinHeight = 32;
        categoryActions.Children.Add(_assignmentCategoryComboBox);

        _assignButton.MinWidth = 84;
        _assignButton.Padding = new Thickness(12, 6, 12, 6);
        _assignButton.Margin = new Thickness(8, 0, 0, 0);
        _assignButton.Click += (_, _) => AssignSelectedFloorCategory();
        categoryActions.Children.Add(_assignButton);

        _clearAssignmentButton.MinWidth = 108;
        _clearAssignmentButton.Padding = new Thickness(12, 6, 12, 6);
        _clearAssignmentButton.Margin = new Thickness(8, 0, 0, 0);
        _clearAssignmentButton.Click += (_, _) => ClearSelectedFloorCategoryOverride();
        categoryActions.Children.Add(_clearAssignmentButton);

        content.Children.Add(categoryActions);

        StackPanel persistenceActions = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
        };

        _saveAssignmentsButton.MinWidth = 132;
        _saveAssignmentsButton.Padding = new Thickness(12, 6, 12, 6);
        _saveAssignmentsButton.Click += (_, _) => SavePendingAssignments();
        persistenceActions.Children.Add(_saveAssignmentsButton);

        _discardAssignmentsButton.MinWidth = 132;
        _discardAssignmentsButton.Padding = new Thickness(12, 6, 12, 6);
        _discardAssignmentsButton.Margin = new Thickness(8, 0, 0, 0);
        _discardAssignmentsButton.Click += (_, _) => DiscardPendingAssignments();
        persistenceActions.Children.Add(_discardAssignmentsButton);

        content.Children.Add(persistenceActions);

        _assignmentHintText.Margin = new Thickness(0, 12, 0, 0);
        _assignmentHintText.Foreground = WpfDialogChrome.MutedTextBrush;
        _assignmentHintText.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(_assignmentHintText);

        ApplyAssignmentsLanguage();
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content,
        };
    }

    private UIElement BuildWarningsTab()
    {
        StackPanel content = new()
        {
            Margin = new Thickness(0, 12, 0, 0),
        };

        _warningsSummaryText.Foreground = WpfDialogChrome.MutedTextBrush;
        _warningsSummaryText.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(_warningsSummaryText);

        _warningsListBox.Margin = new Thickness(0, 10, 0, 0);
        _warningsListBox.MinHeight = 200;
        content.Children.Add(_warningsListBox);

        _warningsEmptyText.Margin = new Thickness(0, 8, 0, 0);
        _warningsEmptyText.Foreground = WpfDialogChrome.MutedTextBrush;
        _warningsEmptyText.TextWrapping = TextWrapping.Wrap;
        _warningsEmptyText.Visibility = Visibility.Collapsed;
        content.Children.Add(_warningsEmptyText);

        return content;
    }

    private WpfGrid BuildAssignmentSummaryGrid()
    {
        WpfGrid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddSummaryRow(grid, 0, T("Floor type", "床タイプ"), _assignmentTargetValueText);
        AddSummaryRow(grid, 1, T("Parsed candidate", "解析候補"), _assignmentCandidateValueText);
        AddSummaryRow(grid, 2, T("Current resolution", "現在の解決結果"), _assignmentCurrentValueText);
        return grid;
    }

    private UIElement BuildFooter()
    {
        Border footerBorder = new()
        {
            BorderBrush = WpfDialogChrome.CardBorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(0, 12, 0, 0),
        };

        DockPanel footer = new() { LastChildFill = false };

        _footerSummaryText.Foreground = WpfDialogChrome.StatusTextBrush;
        _footerSummaryText.FontWeight = FontWeights.SemiBold;
        _footerSummaryText.TextWrapping = TextWrapping.Wrap;
        DockPanel.SetDock(_footerSummaryText, Dock.Left);
        footer.Children.Add(_footerSummaryText);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        DockPanel.SetDock(actions, Dock.Right);

        _helpButton.MinWidth = 88;
        _helpButton.Padding = new Thickness(12, 6, 12, 6);
        _helpButton.Click += (_, _) => ShowHelp();
        actions.Children.Add(_helpButton);

        _closeButton.MinWidth = 96;
        _closeButton.Padding = new Thickness(12, 6, 12, 6);
        _closeButton.Margin = new Thickness(8, 0, 0, 0);
        _closeButton.IsCancel = true;
        _closeButton.Click += (_, _) => _window.Close();
        actions.Children.Add(_closeButton);

        footer.Children.Add(actions);
        footerBorder.Child = footer;

        _helpButton.Content = L("Common.Help", "Help");
        _closeButton.Content = T("Close", "閉じる");
        return footerBorder;
    }

    private void LoadRequest(ExportPreviewRequest request)
    {
        _unitsCheckBox.IsChecked = request.FeatureTypes.HasFlag(ExportFeatureType.Unit);
        _openingsCheckBox.IsChecked = request.FeatureTypes.HasFlag(ExportFeatureType.Opening);
        _detailsCheckBox.IsChecked = request.FeatureTypes.HasFlag(ExportFeatureType.Detail);
        _levelsCheckBox.IsChecked = false;
        _basemapCheckBox.IsChecked = false;
        _basemapCheckBox.IsEnabled = false;
        _surveyPointCheckBox.IsChecked = false;
        _surveyPointCheckBox.IsEnabled = false;

        _assignmentCategoryComboBox.Items.Clear();
        foreach (string category in _controller.SupportedFloorCategories)
        {
            _assignmentCategoryComboBox.Items.Add(category);
        }

        if (_assignmentCategoryComboBox.Items.Count > 0)
        {
            _assignmentCategoryComboBox.SelectedIndex = 0;
        }

        _viewComboBox.Items.Clear();
        foreach (ViewPlan view in request.SelectedViews)
        {
            _viewComboBox.Items.Add(new ViewItem(view, _controller.BuildViewDisplayText(view)));
        }

        if (_viewComboBox.Items.Count > 0)
        {
            _viewComboBox.SelectedIndex = 0;
        }
    }

    private void LoadSelectedView(bool fitViewport)
    {
        if (_viewComboBox.SelectedItem is not ViewItem item)
        {
            return;
        }

        if (!_controller.IsViewCached(item.View))
        {
            _footerSummaryText.Text = _controller.BuildLoadingStatus(item.View);
            _window.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        }

        PreviewDisplayViewState displayState = _controller.LoadView(item.View);
        ApplyLoadedDisplayState(displayState, fitViewport);
    }

    private void ApplyLoadedDisplayState(PreviewDisplayViewState displayState, bool fitViewport)
    {
        _isLoadingView = true;
        try
        {
            _canvas.ConfigureBasemap(displayState.MapContext, _controller.BasemapSettings);
            _canvas.SurveyPointMarkerLabel = "0,0";
            _canvas.SetViewData(displayState.DisplayFeatures, displayState.DisplayBounds, displayState.DisplaySurveyPoint);
            PopulateViewSummary();
            UpdateBasemapAvailability();
            UpdateSurveyPointAvailability();
            ApplyCanvasFilters();
            if (fitViewport)
            {
                _canvas.RequestFitToFeatures();
            }

            PopulateLegend();
            PopulateWarnings();
            PopulateUnassignedFloors();
            UpdateDetails(null);
            UpdateAssignmentControls();
            RefreshFooterStatus();
        }
        finally
        {
            _isLoadingView = false;
        }
    }

    private void PopulateViewSummary()
    {
        _viewSummaryText.Text = _controller.BuildQuickSummaryText();
        _viewCoordinateText.Text = _controller.BuildCoordinateSummaryText();
        _viewHelperText.Text = _controller.BuildViewInstructionText();
    }

    private void PopulateLegend()
    {
        _legendPanel.Children.Clear();
        var entries = _controller.GetLegendEntries();
        if (entries.Count == 0)
        {
            _legendEmptyText.Text = _controller.BuildLegendEmptyText();
            _legendEmptyText.Visibility = Visibility.Visible;
            return;
        }

        _legendEmptyText.Visibility = Visibility.Collapsed;
        foreach (PreviewLegendEntry entry in entries)
        {
            _legendPanel.Children.Add(BuildLegendItem(entry));
        }
    }

    private void PopulateWarnings()
    {
        _warningsSummaryText.Text = _controller.BuildWarningsSummaryText();
        _warningsListBox.Items.Clear();

        var warnings = _controller.GetWarnings();
        if (warnings.Count == 0)
        {
            _warningsEmptyText.Text = _controller.BuildNoWarningsText();
            _warningsEmptyText.Visibility = Visibility.Visible;
            _warningsListBox.Visibility = Visibility.Collapsed;
            return;
        }

        _warningsEmptyText.Visibility = Visibility.Collapsed;
        _warningsListBox.Visibility = Visibility.Visible;
        foreach (string warning in warnings)
        {
            _warningsListBox.Items.Add(warning);
        }
    }

    private void PopulateUnassignedFloors()
    {
        string? selectedFloorType = _controller.GetAssignmentState().SelectedFloorTypeName;
        _suppressUnassignedSelectionChanged = true;
        try
        {
            _unassignedFloorsListBox.Items.Clear();
            foreach (PreviewUnassignedFloorGroup group in _controller.GetUnassignedFloors())
            {
                _unassignedFloorsListBox.Items.Add(new UnassignedFloorItem(group));
            }

            _unassignedEmptyText.Text = _controller.BuildNoUnassignedFloorsText();
            _unassignedEmptyText.Visibility = _unassignedFloorsListBox.Items.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            SelectUnassignedFloorItem(selectedFloorType);
        }
        finally
        {
            _suppressUnassignedSelectionChanged = false;
        }
    }

    private void UpdateDetails(PreviewFeatureData? feature)
    {
        PreviewDetailsSnapshot snapshot = _controller.BuildDetailsSnapshot(feature);
        _detailsPanel.Children.Clear();
        _detailsPlaceholderText.Text = snapshot.HelperText;
        _detailsPlaceholderText.Visibility = string.IsNullOrWhiteSpace(snapshot.HelperText)
            ? Visibility.Collapsed
            : Visibility.Visible;

        foreach (PreviewDetailEntry entry in snapshot.Entries)
        {
            _detailsPanel.Children.Add(BuildDetailRow(entry));
        }
    }

    private void UpdateAssignmentControls()
    {
        PreviewAssignmentState state = _controller.GetAssignmentState();

        _assignmentPendingText.Text = state.PendingMessage;
        _assignmentPendingText.Foreground = state.HasPendingChanges
            ? WpfDialogChrome.StatusWarningTextBrush
            : WpfDialogChrome.MutedTextBrush;
        _assignmentPendingBadge.Background = state.HasPendingChanges
            ? WpfDialogChrome.StatusWarningBackgroundBrush
            : WpfDialogChrome.StatusBackgroundBrush;
        _assignmentPendingBadge.BorderBrush = state.HasPendingChanges
            ? WpfDialogChrome.StatusWarningBorderBrush
            : WpfDialogChrome.CardBorderBrush;
        _assignmentPendingBadge.BorderThickness = new Thickness(1);

        _assignmentSourceText.Text = T("Assignment source", "割り当て元") + ": " + _controller.AssignmentSourceLabel;
        _assignmentTargetValueText.Text = state.TargetFloorType;
        _assignmentCandidateValueText.Text = state.ParsedCandidate;
        _assignmentCurrentValueText.Text = state.CurrentResolution;
        _assignmentCategoryComboBox.IsEnabled = state.CanChooseCategory;
        SelectAssignmentCategory(state.SuggestedCategory);
        _assignButton.IsEnabled = state.CanAssign;
        _clearAssignmentButton.IsEnabled = state.CanClear;
        _saveAssignmentsButton.IsEnabled = state.CanSave;
        _discardAssignmentsButton.IsEnabled = state.CanDiscard;
        _assignmentHintText.Text = state.HintText;
        SelectUnassignedFloorItem(state.SelectedFloorTypeName);
    }

    private void ApplyCanvasFilters()
    {
        _canvas.ShowUnits = _unitsCheckBox.IsChecked == true;
        _canvas.ShowOpenings = _openingsCheckBox.IsChecked == true;
        _canvas.ShowDetails = _detailsCheckBox.IsChecked == true;
        _canvas.ShowLevels = _levelsCheckBox.IsChecked == true;
        _canvas.ShowStairs = _stairsCheckBox.IsChecked == true;
        _canvas.ShowEscalators = _escalatorsCheckBox.IsChecked == true;
        _canvas.ShowElevators = _elevatorsCheckBox.IsChecked == true;
        _canvas.ShowWarningsOnly = _warningsOnlyCheckBox.IsChecked == true;
        _canvas.ShowOverriddenOnly = _overriddenOnlyCheckBox.IsChecked == true;
        _canvas.ShowUnassignedOnly = _unassignedOnlyCheckBox.IsChecked == true;
        _canvas.ShowBasemap = _basemapCheckBox.IsEnabled && _basemapCheckBox.IsChecked == true;
        _canvas.ShowSurveyPoint = _surveyPointCheckBox.IsEnabled && _surveyPointCheckBox.IsChecked == true;
        _canvas.SearchText = _searchTextBox.Text ?? string.Empty;
        _canvas.RefreshFilters();
        RefreshFooterStatus();
    }

    private void UpdateBasemapAvailability()
    {
        bool available = _controller.IsBasemapToggleAvailable(_canvas.BasemapAvailable);
        _basemapCheckBox.IsEnabled = available;
        if (!available)
        {
            _basemapCheckBox.IsChecked = false;
        }

        _canvas.ShowBasemap = available && _basemapCheckBox.IsChecked == true;
        UpdateInlineAvailabilityHints();
    }

    private void UpdateSurveyPointAvailability()
    {
        bool available = _controller.IsSurveyPointToggleAvailable(_canvas.SurveyPointAvailable);
        _surveyPointCheckBox.IsEnabled = available;
        if (!available)
        {
            _surveyPointCheckBox.IsChecked = false;
        }

        _canvas.ShowSurveyPoint = available && _surveyPointCheckBox.IsChecked == true;
        UpdateInlineAvailabilityHints();
    }

    private void UpdateInlineAvailabilityHints()
    {
        SetHintText(
            _basemapHintText,
            _controller.GetBasemapInlineMessage(_canvas.BasemapAvailable, _canvas.BasemapUnavailableReason));
        SetHintText(
            _surveyPointHintText,
            _controller.GetSurveyPointInlineMessage(_canvas.SurveyPointAvailable));
        RefreshFooterStatus();
    }

    private void RefreshFooterStatus()
    {
        _footerSummaryText.Text = _controller.BuildFooterStatus(
            _canvas.BasemapAvailable,
            _canvas.BasemapUnavailableReason,
            _canvas.BasemapAttribution,
            _canvas.ShowBasemap,
            _canvas.SurveyPointAvailable,
            _canvas.ShowSurveyPoint);
    }

    private void OnSelectedFeatureChanged(PreviewFeatureData? feature)
    {
        if (_isLoadingView && feature == null)
        {
            return;
        }

        UpdateDetails(feature);
        _controller.SelectFeature(feature);
        UpdateAssignmentControls();
    }

    private void OnUnassignedFloorSelectionChanged()
    {
        if (_suppressUnassignedSelectionChanged)
        {
            return;
        }

        if (_unassignedFloorsListBox.SelectedItem is not UnassignedFloorItem item)
        {
            _controller.SelectUnassignedFloor(null, null);
            UpdateAssignmentControls();
            return;
        }

        _controller.SelectUnassignedFloor(item.Group.FloorTypeName, item.Group.ParsedZoneCandidate);
        UpdateAssignmentControls();
    }

    private void AssignSelectedFloorCategory()
    {
        if (_assignmentCategoryComboBox.SelectedItem is not string category || string.IsNullOrWhiteSpace(category))
        {
            return;
        }

        var floorTypeNames = _controller.GetSelectedFloorTypeNames(
            _unassignedFloorsListBox.SelectedItems.Cast<object>().OfType<UnassignedFloorItem>().Select(item => item.Group.FloorTypeName));
        PreviewDisplayViewState? displayState = _controller.StageCategoryOverride(floorTypeNames, category);
        if (displayState != null)
        {
            ApplyLoadedDisplayState(displayState, fitViewport: false);
        }
        else
        {
            RefreshFooterStatus();
        }
    }

    private void ClearSelectedFloorCategoryOverride()
    {
        var floorTypeNames = _controller.GetSelectedFloorTypeNames(
            _unassignedFloorsListBox.SelectedItems.Cast<object>().OfType<UnassignedFloorItem>().Select(item => item.Group.FloorTypeName));
        PreviewDisplayViewState? displayState = _controller.ClearCategoryOverride(floorTypeNames);
        if (displayState != null)
        {
            ApplyLoadedDisplayState(displayState, fitViewport: false);
        }
        else
        {
            RefreshFooterStatus();
        }
    }

    private void SavePendingAssignments()
    {
        PreviewDisplayViewState? displayState = _controller.SavePendingAssignments();
        if (displayState != null)
        {
            ApplyLoadedDisplayState(displayState, fitViewport: false);
        }
    }

    private void DiscardPendingAssignments()
    {
        PreviewDisplayViewState? displayState = _controller.DiscardPendingAssignments();
        if (displayState != null)
        {
            ApplyLoadedDisplayState(displayState, fitViewport: false);
        }
    }

    private void SelectAssignmentCategory(string? category)
    {
        if (_assignmentCategoryComboBox.Items.Count == 0)
        {
            return;
        }

        string trimmedCategory = (category ?? string.Empty).Trim();
        for (int i = 0; i < _assignmentCategoryComboBox.Items.Count; i++)
        {
            if (string.Equals(
                    _assignmentCategoryComboBox.Items[i]?.ToString(),
                    trimmedCategory,
                    StringComparison.OrdinalIgnoreCase))
            {
                _assignmentCategoryComboBox.SelectedIndex = i;
                return;
            }
        }

        if (_assignmentCategoryComboBox.SelectedIndex < 0)
        {
            _assignmentCategoryComboBox.SelectedIndex = 0;
        }
    }

    private void SelectUnassignedFloorItem(string? floorTypeName)
    {
        _suppressUnassignedSelectionChanged = true;
        try
        {
            if (string.IsNullOrWhiteSpace(floorTypeName))
            {
                _unassignedFloorsListBox.UnselectAll();
                return;
            }

            for (int i = 0; i < _unassignedFloorsListBox.Items.Count; i++)
            {
                if (_unassignedFloorsListBox.Items[i] is UnassignedFloorItem item &&
                    string.Equals(item.Group.FloorTypeName, floorTypeName, StringComparison.Ordinal))
                {
                    _unassignedFloorsListBox.SelectedIndex = i;
                    _unassignedFloorsListBox.ScrollIntoView(_unassignedFloorsListBox.Items[i]);
                    return;
                }
            }

            _unassignedFloorsListBox.UnselectAll();
        }
        finally
        {
            _suppressUnassignedSelectionChanged = false;
        }
    }

    private void OnBasemapStatusChanged(string? message)
    {
        _controller.UpdateBasemapProviderStatus(message);
        RefreshFooterStatus();
    }

    private void ShowHelp()
    {
        HelpLauncher.Show(TryGetOwnerWindow(), HelpTopic.PreviewAndAssignments, _controller.Language, _window.Title);
        _window.Activate();
    }

    private WinForms.IWin32Window? TryGetOwnerWindow()
    {
        IntPtr handle = new WindowInteropHelper(_window).EnsureHandle();
        return handle == IntPtr.Zero ? null : new Win32WindowOwner(handle);
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _controller.DiscardPendingChangesOnClose();
    }

    private void ConfigureLayerCheckBox(CheckBox checkBox, string text)
    {
        checkBox.Content = text;
        checkBox.Margin = new Thickness(0, 0, 0, 8);
        checkBox.Checked += (_, _) => ApplyCanvasFilters();
        checkBox.Unchecked += (_, _) => ApplyCanvasFilters();
    }

    private static TextBlock CreateHintText(TextBlock textBlock, Thickness margin)
    {
        textBlock.Margin = margin;
        textBlock.Foreground = WpfDialogChrome.MutedTextBrush;
        textBlock.TextWrapping = TextWrapping.Wrap;
        textBlock.Visibility = Visibility.Collapsed;
        return textBlock;
    }

    private static void SetHintText(TextBlock textBlock, string text)
    {
        textBlock.Text = text ?? string.Empty;
        textBlock.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void AddSummaryRow(WpfGrid grid, int rowIndex, string label, TextBlock valueBlock)
    {
        TextBlock labelBlock = new()
        {
            Text = label,
            Margin = new Thickness(0, rowIndex == 0 ? 0 : 8, 12, 0),
            Foreground = WpfDialogChrome.MutedTextBrush,
            FontWeight = FontWeights.SemiBold,
        };
        Grid.SetRow(labelBlock, rowIndex);
        grid.Children.Add(labelBlock);

        valueBlock.Margin = new Thickness(0, rowIndex == 0 ? 0 : 8, 0, 0);
        valueBlock.TextWrapping = TextWrapping.Wrap;
        valueBlock.Foreground = WpfDialogChrome.StatusTextBrush;
        Grid.SetRow(valueBlock, rowIndex);
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(valueBlock);
    }

    private Border BuildDetailRow(PreviewDetailEntry entry)
    {
        Border border = new()
        {
            BorderBrush = WpfDialogChrome.CardBorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 10),
            Margin = new Thickness(0, 0, 0, 10),
        };

        WpfGrid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        TextBlock label = new()
        {
            Text = entry.Label,
            Foreground = WpfDialogChrome.MutedTextBrush,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 12, 0),
        };
        grid.Children.Add(label);

        TextBlock value = new()
        {
            Text = entry.Value,
            TextWrapping = TextWrapping.Wrap,
            Foreground = WpfDialogChrome.StatusTextBrush,
        };
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);

        border.Child = grid;
        return border;
    }

    private UIElement BuildLegendItem(PreviewLegendEntry entry)
    {
        StackPanel item = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4),
        };

        Border swatch = new()
        {
            Width = 14,
            Height = 14,
            Margin = new Thickness(0, 2, 6, 0),
            BorderBrush = WpfDialogChrome.CardBorderBrush,
            BorderThickness = new Thickness(1),
            Background = CreateBrush(entry.FillColorHex, Colors.LightGray),
        };
        item.Children.Add(swatch);

        item.Children.Add(new TextBlock
        {
            Text = $"{entry.Label} ({entry.Count})",
            Foreground = WpfDialogChrome.StatusTextBrush,
        });

        return item;
    }

    private static Brush CreateBrush(string hex, System.Windows.Media.Color fallback)
    {
        string normalized = (hex ?? string.Empty).Trim().TrimStart('#');
        if (normalized.Length != 6)
        {
            return new SolidColorBrush(fallback);
        }

        try
        {
            byte red = byte.Parse(normalized.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte green = byte.Parse(normalized.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte blue = byte.Parse(normalized.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new SolidColorBrush(Color.FromRgb(red, green, blue));
        }
        catch
        {
            return new SolidColorBrush(fallback);
        }
    }

    private void ApplySearchLanguage(TextBlock title, TextBlock description, TextBlock label)
    {
        title.Text = T("Search", "検索");
        description.Text = T(
            "Find features by name, category, or export ID.",
            "名前、カテゴリ、または export ID で要素を検索します。");
        label.Text = T("Search text", "検索テキスト");
    }

    private void ApplyMapLayersLanguage(TextBlock title, TextBlock description)
    {
        title.Text = T("Map layers", "マップ レイヤー");
        description.Text = T(
            "Choose which preview layers are visible on the map.",
            "地図上に表示するプレビュー レイヤーを選びます。");
    }

    private void ApplyFiltersLanguage(TextBlock title, TextBlock description)
    {
        title.Text = T("Filters", "フィルター");
        description.Text = T(
            "Focus the preview on warnings, overrides, or unresolved content.",
            "警告、上書き、未解決の内容に絞って表示します。");
    }

    private void ApplyViewToolsLanguage(TextBlock title, TextBlock description)
    {
        title.Text = T("View tools", "表示ツール");
        description.Text = T(
            "Fit the current preview or reset the map position.",
            "現在のプレビューに合わせるか、表示位置をリセットします。");
        _fitButton.Content = T("Fit", "全体表示");
        _resetButton.Content = T("Reset", "リセット");
    }

    private void ApplyAssignmentsLanguage()
    {
        _assignButton.Content = T("Assign", "割り当て");
        _clearAssignmentButton.Content = T("Clear override", "上書きを解除");
        _saveAssignmentsButton.Content = T("Save assignments", "割り当てを保存");
        _discardAssignmentsButton.Content = T("Discard pending", "保留を破棄");
    }

    private string T(string english, string japanese)
    {
        return UiLanguageText.Select(_controller.Language, english, japanese);
    }

    private string L(string key, string fallback)
    {
        return RevitGeoExporter.Resources.LocalizedTextProvider.Get(_controller.Language, key, fallback);
    }

    private sealed class ViewItem
    {
        public ViewItem(ViewPlan view, string displayText)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
            DisplayText = displayText ?? throw new ArgumentNullException(nameof(displayText));
        }

        public ViewPlan View { get; }

        public string DisplayText { get; }

        public override string ToString() => DisplayText;
    }

    private sealed class UnassignedFloorItem
    {
        public UnassignedFloorItem(PreviewUnassignedFloorGroup group)
        {
            Group = group ?? throw new ArgumentNullException(nameof(group));
        }

        public PreviewUnassignedFloorGroup Group { get; }

        public override string ToString()
        {
            return $"{Group.FloorTypeName} ({Group.UnitCount})";
        }
    }

    private sealed class Win32WindowOwner : WinForms.IWin32Window
    {
        public Win32WindowOwner(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }
    }
}
