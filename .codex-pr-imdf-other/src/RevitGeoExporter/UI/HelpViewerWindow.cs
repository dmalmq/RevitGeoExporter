using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms.Integration;
using System.Windows.Interop;
using System.Windows.Media;
using RevitGeoExporter.Help;
using RevitGeoExporter.Resources;
using Grid = System.Windows.Controls.Grid;
using WinForms = System.Windows.Forms;

namespace RevitGeoExporter.UI;

internal sealed class HelpViewerWindow : IDisposable
{
    private readonly HelpContentProvider _provider;
    private readonly string? _contextLabel;
    private readonly Window _window;
    private readonly ListBox _topicListBox = new();
    private readonly WinForms.WebBrowser _browser = new();
    private readonly ComboBox _languageComboBox = new();
    private readonly TextBlock _headerTitleText = new();
    private readonly TextBlock _headerDescriptionText = new();
    private readonly TextBlock _currentTopicText = new();
    private readonly TextBlock _contextText = new();
    private readonly TextBlock _navigationTitleText = new();
    private readonly TextBlock _navigationDescriptionText = new();
    private readonly TextBlock _articleTitleText = new();
    private readonly TextBlock _articleDescriptionText = new();
    private readonly TextBlock _footerSummaryText = new();
    private readonly TextBlock _versionText = new();
    private readonly TextBlock _languageLabel = new();
    private readonly Button _closeButton = new();

    private HelpLanguage _language;
    private HelpTopic _currentTopic;
    private HelpDocument? _currentDocument;
    private bool _isSynchronizing;

    public HelpViewerWindow(
        HelpContentProvider provider,
        HelpTopic initialTopic,
        HelpLanguage initialLanguage,
        string? contextLabel = null,
        WinForms.IWin32Window? owner = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _currentTopic = initialTopic;
        _language = initialLanguage;
        _contextLabel = string.IsNullOrWhiteSpace(contextLabel) ? null : contextLabel!.Trim();

        _window = new Window
        {
            Width = 1280,
            Height = 900,
            MinWidth = 1080,
            MinHeight = 720,
            Background = WpfDialogChrome.WindowBackgroundBrush,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Content = BuildLayout(),
        };

        if (owner != null && owner.Handle != IntPtr.Zero)
        {
            new WindowInteropHelper(_window).Owner = owner.Handle;
        }

        PopulateLanguageChoices();
        ApplyLanguage();
        LoadTopics();
        LoadTopic(initialTopic);
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

        _browser.Dispose();
    }

    private UIElement BuildLayout()
    {
        Grid root = new()
        {
            Margin = new Thickness(16),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(BuildHeaderCard());

        Grid body = new()
        {
            Margin = new Thickness(0, 12, 0, 0),
        };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        UIElement navigationCard = BuildNavigationCard();
        body.Children.Add(navigationCard);

        UIElement articleCard = BuildArticleCard();
        Grid.SetColumn(articleCard, 1);
        if (articleCard is FrameworkElement articleElement)
        {
            articleElement.Margin = new Thickness(12, 0, 0, 0);
        }

        body.Children.Add(articleCard);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        UIElement footer = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }

    private UIElement BuildHeaderCard()
    {
        Border card = WpfDialogChrome.CreateCard();
        StackPanel layout = new();

        _headerTitleText.FontSize = 24;
        _headerTitleText.FontWeight = FontWeights.SemiBold;
        _headerTitleText.Foreground = WpfDialogChrome.StatusTextBrush;
        layout.Children.Add(_headerTitleText);

        WpfDialogChrome.StyleDescriptionText(_headerDescriptionText);
        layout.Children.Add(_headerDescriptionText);

        Border topicCard = new()
        {
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(12),
            BorderBrush = WpfDialogChrome.CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = WpfDialogChrome.StatusBackgroundBrush,
        };

        StackPanel topicLayout = new();
        _currentTopicText.FontSize = 16;
        _currentTopicText.FontWeight = FontWeights.SemiBold;
        _currentTopicText.Foreground = WpfDialogChrome.StatusTextBrush;
        topicLayout.Children.Add(_currentTopicText);

        _contextText.Margin = new Thickness(0, 6, 0, 0);
        _contextText.TextWrapping = TextWrapping.Wrap;
        _contextText.Foreground = WpfDialogChrome.MutedTextBrush;
        topicLayout.Children.Add(_contextText);

        topicCard.Child = topicLayout;
        layout.Children.Add(topicCard);
        card.Child = layout;
        return card;
    }

    private UIElement BuildNavigationCard()
    {
        _topicListBox.BorderThickness = new Thickness(0);
        _topicListBox.Background = Brushes.Transparent;
        _topicListBox.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _topicListBox.SelectionChanged += (_, _) =>
        {
            if (_isSynchronizing)
            {
                return;
            }

            if (_topicListBox.SelectedItem is TopicItem item)
            {
                LoadTopic(item.Topic);
            }
        };
        _topicListBox.ItemTemplate = BuildTopicTemplate();
        _topicListBox.ItemContainerStyle = BuildTopicItemStyle();
        _topicListBox.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);

        Border listBorder = new()
        {
            BorderBrush = WpfDialogChrome.CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.White,
            Padding = new Thickness(6),
            Child = _topicListBox,
        };

        return WpfDialogChrome.CreateSectionCard(_navigationTitleText, _navigationDescriptionText, listBorder);
    }

    private UIElement BuildArticleCard()
    {
        _browser.AllowWebBrowserDrop = false;
        _browser.IsWebBrowserContextMenuEnabled = false;
        _browser.WebBrowserShortcutsEnabled = true;
        _browser.ScriptErrorsSuppressed = true;
        _browser.Dock = WinForms.DockStyle.Fill;
        _browser.Navigating += OnBrowserNavigating;

        Border card = WpfDialogChrome.CreateCard(new Thickness(0));
        Grid layout = new();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        StackPanel header = new()
        {
            Margin = new Thickness(16, 16, 16, 12),
        };
        WpfDialogChrome.StyleSectionTitle(_articleTitleText);
        WpfDialogChrome.StyleDescriptionText(_articleDescriptionText);
        header.Children.Add(_articleTitleText);
        header.Children.Add(_articleDescriptionText);
        layout.Children.Add(header);

        Border browserBorder = new()
        {
            Margin = new Thickness(16, 0, 16, 16),
            BorderBrush = WpfDialogChrome.CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.White,
        };

        browserBorder.Child = new WindowsFormsHost
        {
            Child = _browser,
        };

        Grid.SetRow(browserBorder, 1);
        layout.Children.Add(browserBorder);

        card.Child = layout;
        return card;
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

        StackPanel left = new();
        _footerSummaryText.FontWeight = FontWeights.SemiBold;
        _footerSummaryText.Foreground = WpfDialogChrome.StatusTextBrush;
        _footerSummaryText.TextWrapping = TextWrapping.Wrap;
        left.Children.Add(_footerSummaryText);

        StackPanel meta = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        _versionText.Foreground = WpfDialogChrome.MutedTextBrush;
        _versionText.VerticalAlignment = VerticalAlignment.Center;
        meta.Children.Add(_versionText);

        _languageLabel.Margin = new Thickness(16, 0, 8, 0);
        _languageLabel.Foreground = WpfDialogChrome.MutedTextBrush;
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

        _closeButton.MinWidth = 96;
        _closeButton.Padding = new Thickness(12, 6, 12, 6);
        _closeButton.IsDefault = true;
        _closeButton.IsCancel = true;
        _closeButton.Click += (_, _) =>
        {
            _window.DialogResult = true;
            _window.Close();
        };
        actions.Children.Add(_closeButton);

        footer.Children.Add(actions);
        footerBorder.Child = footer;
        return footerBorder;
    }

    private static DataTemplate BuildTopicTemplate()
    {
        FrameworkElementFactory label = new(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(TopicItem.Label)));
        label.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        label.SetValue(TextBlock.ForegroundProperty, WpfDialogChrome.StatusTextBrush);

        return new DataTemplate
        {
            VisualTree = label,
        };
    }

    private static Style BuildTopicItemStyle()
    {
        Style style = new(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 6)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));

        Trigger hoverTrigger = new() { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, WpfDialogChrome.StatusBackgroundBrush));
        hoverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, WpfDialogChrome.CardBorderBrush));
        style.Triggers.Add(hoverTrigger);

        Trigger selectedTrigger = new() { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, WpfDialogChrome.StatusBackgroundBrush));
        selectedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, WpfDialogChrome.CardBorderBrush));
        selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, WpfDialogChrome.StatusTextBrush));
        style.Triggers.Add(selectedTrigger);

        return style;
    }

    private void PopulateLanguageChoices()
    {
        _languageComboBox.Items.Clear();
        _languageComboBox.Items.Add(new LanguageItem(HelpLanguage.English));
        _languageComboBox.Items.Add(new LanguageItem(HelpLanguage.Japanese));
    }

    private void ApplyLanguage()
    {
        _isSynchronizing = true;
        try
        {
            LanguageItem.DisplayLanguage = HelpContentProvider.ToUiLanguage(_language);
            _languageComboBox.Items.Refresh();
            _languageComboBox.SelectedItem = _languageComboBox.Items
                .OfType<LanguageItem>()
                .FirstOrDefault(item => item.Language == _language);

            _headerTitleText.Text = L("Common.Help", "Help");
            _headerDescriptionText.Text = L(
                "Help.Viewer.HeaderDescription",
                "Offline guidance for export, preview, settings, and troubleshooting.");
            _navigationTitleText.Text = L("Help.Viewer.NavigationTitle", "Topics");
            _navigationDescriptionText.Text = L(
                "Help.Viewer.NavigationDescription",
                "Choose a topic to review the current workflow and related guidance.");
            _footerSummaryText.Text = L("Help.Viewer.FooterSummary", "Offline bilingual help");
            _versionText.Text = string.Format(L("Common.Version", "Version {0}"), ProjectInfo.VersionTag);
            _languageLabel.Text = L("Help.Viewer.LanguageLabel", "Language");
            _closeButton.Content = L("Common.Close", "Close");

            RefreshHeader();
            RefreshArticleHeader();
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private void LoadTopics()
    {
        _isSynchronizing = true;
        try
        {
            _topicListBox.Items.Clear();
            foreach (HelpTopic topic in _provider.GetTopicList(_language))
            {
                _topicListBox.Items.Add(new TopicItem(topic, _provider.GetTopicLabel(topic, _language)));
            }

            TopicItem? selectedItem = _topicListBox.Items
                .OfType<TopicItem>()
                .FirstOrDefault(item => item.Topic == _currentTopic);
            if (selectedItem != null)
            {
                _topicListBox.SelectedItem = selectedItem;
            }
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private void LoadTopic(HelpTopic topic)
    {
        _isSynchronizing = true;
        try
        {
            _currentTopic = topic;
            _currentDocument = _provider.GetDocument(topic, _language);
            _browser.DocumentText = _currentDocument.Html;
            _window.Title = string.Format(
                L("Help.Viewer.Title", "Help - {0}"),
                _currentDocument.Title);

            TopicItem? selectedItem = _topicListBox.Items
                .OfType<TopicItem>()
                .FirstOrDefault(item => item.Topic == topic);
            if (selectedItem != null)
            {
                _topicListBox.SelectedItem = selectedItem;
            }

            RefreshHeader();
            RefreshArticleHeader();
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private void OnLanguageChanged()
    {
        if (_isSynchronizing || _languageComboBox.SelectedItem is not LanguageItem item || item.Language == _language)
        {
            return;
        }

        _language = item.Language;
        ApplyLanguage();
        LoadTopics();
        LoadTopic(_currentTopic);
    }

    private void RefreshHeader()
    {
        _currentTopicText.Text = _provider.GetTopicLabel(_currentTopic, _language);

        if (string.IsNullOrWhiteSpace(_contextLabel))
        {
            _contextText.Visibility = Visibility.Collapsed;
            _contextText.Text = string.Empty;
            return;
        }

        _contextText.Visibility = Visibility.Visible;
        _contextText.Text = string.Format(
            L("Help.Viewer.ContextLabel", "Opened from: {0}"),
            _contextLabel);
    }

    private void RefreshArticleHeader()
    {
        _articleTitleText.Text = _currentDocument?.Title ?? _provider.GetTopicLabel(_currentTopic, _language);
        _articleDescriptionText.Text = _currentDocument?.IsFallback == true
            ? L(
                "Help.Viewer.FallbackNotice",
                "Showing fallback content because this topic is not available in the selected language.")
            : L(
                "Help.Viewer.ArticleDescription",
                "Articles open inside this viewer, including links to related help topics.");
    }

    private void OnBrowserNavigating(object? sender, WinForms.WebBrowserNavigatingEventArgs e)
    {
        if (!HelpTopicLinkParser.TryParse(e.Url, out HelpTopic topic))
        {
            return;
        }

        e.Cancel = true;
        LoadTopic(topic);
    }

    private string L(string key, string fallback)
    {
        return LocalizedTextProvider.Get(HelpContentProvider.ToUiLanguage(_language), key, fallback);
    }

    private sealed class TopicItem
    {
        public TopicItem(HelpTopic topic, string label)
        {
            Topic = topic;
            Label = label;
        }

        public HelpTopic Topic { get; }

        public string Label { get; }
    }

    private sealed class LanguageItem
    {
        public static UiLanguage DisplayLanguage { get; set; } = UiLanguage.English;

        public LanguageItem(HelpLanguage language)
        {
            Language = language;
        }

        public HelpLanguage Language { get; }

        public override string ToString()
        {
            return Language == HelpLanguage.Japanese
                ? LocalizedTextProvider.Get(DisplayLanguage, "Language.Japanese", "Japanese")
                : LocalizedTextProvider.Get(DisplayLanguage, "Language.English", "English");
        }
    }
}
