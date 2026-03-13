using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RevitGeoExporter.Help;
using RevitGeoExporter.Resources;

namespace RevitGeoExporter.UI;

public sealed class HelpViewerForm : Form
{
    private readonly HelpContentProvider _provider;
    private readonly string? _contextLabel;
    private readonly ListBox _topicListBox = new();
    private readonly WebBrowser _browser = new();
    private readonly ComboBox _languageComboBox = new();
    private readonly Label _versionLabel = new();
    private readonly Label _contextLabelControl = new();
    private readonly Button _closeButton = new();
    private HelpLanguage _language;
    private HelpTopic _currentTopic;
    private bool _isLoadingTopic;

    public HelpViewerForm(
        HelpContentProvider provider,
        HelpTopic initialTopic,
        HelpLanguage initialLanguage,
        string? contextLabel = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _currentTopic = initialTopic;
        _language = initialLanguage;
        _contextLabel = string.IsNullOrWhiteSpace(contextLabel) ? null : contextLabel?.Trim();

        InitializeComponents();
        LoadTopics();
        LoadTopic(initialTopic);
    }

    private void InitializeComponents()
    {
        Width = 1220;
        Height = 860;
        MinimumSize = new Size(980, 680);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        Controls.Add(root);

        root.Controls.Add(BuildToolbar(), 0, 0);
        root.Controls.Add(BuildBody(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);
    }

    private Control BuildToolbar()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160f));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Label languageLabel = new()
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = L("Help.Viewer.LanguageLabel", "Language"),
            Padding = new Padding(0, 0, 8, 0),
        };
        panel.Controls.Add(languageLabel, 0, 0);

        _languageComboBox.Dock = DockStyle.Fill;
        _languageComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageComboBox.Items.Add(new LanguageItem(HelpLanguage.English));
        _languageComboBox.Items.Add(new LanguageItem(HelpLanguage.Japanese));
        _languageComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (_languageComboBox.SelectedItem is LanguageItem item)
            {
                _language = item.Language;
                LoadTopics();
                LoadTopic(_currentTopic);
            }
        };
        panel.Controls.Add(_languageComboBox, 1, 0);

        _contextLabelControl.Dock = DockStyle.Fill;
        _contextLabelControl.TextAlign = ContentAlignment.MiddleRight;
        _contextLabelControl.Text = _contextLabel ?? string.Empty;
        panel.Controls.Add(_contextLabelControl, 3, 0);
        return panel;
    }

    private Control BuildBody()
    {
        SplitContainer split = new()
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 280,
        };

        _topicListBox.Dock = DockStyle.Fill;
        _topicListBox.SelectedIndexChanged += (_, _) =>
        {
            if (_isLoadingTopic)
            {
                return;
            }

            if (_topicListBox.SelectedItem is TopicItem item)
            {
                LoadTopic(item.Topic);
            }
        };
        split.Panel1.Controls.Add(_topicListBox);

        _browser.Dock = DockStyle.Fill;
        _browser.AllowWebBrowserDrop = false;
        _browser.IsWebBrowserContextMenuEnabled = false;
        _browser.WebBrowserShortcutsEnabled = true;
        _browser.ScriptErrorsSuppressed = true;
        _browser.Navigating += OnBrowserNavigating;
        split.Panel2.Controls.Add(_browser);

        return split;
    }

    private Control BuildFooter()
    {
        TableLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _versionLabel.Dock = DockStyle.Fill;
        _versionLabel.TextAlign = ContentAlignment.MiddleLeft;
        _versionLabel.Text = string.Format(L("Common.Version", "Version {0}"), ProjectInfo.VersionTag);
        panel.Controls.Add(_versionLabel, 0, 0);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
        };

        _closeButton.Width = 88;
        _closeButton.Height = 28;
        _closeButton.Text = L("Common.Close", "Close");
        _closeButton.DialogResult = DialogResult.OK;
        actions.Controls.Add(_closeButton);
        panel.Controls.Add(actions, 1, 0);

        AcceptButton = _closeButton;
        CancelButton = _closeButton;
        return panel;
    }

    private void LoadTopics()
    {
        _topicListBox.Items.Clear();
        foreach (HelpTopic topic in _provider.GetTopicList(_language))
        {
            _topicListBox.Items.Add(new TopicItem(topic, _provider.GetTopicLabel(topic, _language)));
        }

        for (int i = 0; i < _languageComboBox.Items.Count; i++)
        {
            if (_languageComboBox.Items[i] is LanguageItem item && item.Language == _language)
            {
                _languageComboBox.SelectedIndex = i;
                break;
            }
        }

        for (int i = 0; i < _topicListBox.Items.Count; i++)
        {
            if (_topicListBox.Items[i] is TopicItem item && item.Topic == _currentTopic)
            {
                _topicListBox.SelectedIndex = i;
                break;
            }
        }
    }

    private void LoadTopic(HelpTopic topic)
    {
        _isLoadingTopic = true;
        try
        {
            _currentTopic = topic;
            HelpDocument document = _provider.GetDocument(topic, _language);
            Text = string.Format(
                L("Help.Viewer.Title", "Help - {0}"),
                document.Title);
            _browser.DocumentText = document.Html;

            TopicItem? selectedItem = _topicListBox.Items
                .OfType<TopicItem>()
                .FirstOrDefault(item => item.Topic == topic);
            if (selectedItem != null)
            {
                _topicListBox.SelectedItem = selectedItem;
            }
        }
        finally
        {
            _isLoadingTopic = false;
        }
    }

    private void OnBrowserNavigating(object? sender, WebBrowserNavigatingEventArgs e)
    {
        if (HelpTopicLinkParser.TryParse(e.Url, out HelpTopic topic))
        {
            e.Cancel = true;
            LoadTopic(topic);
        }
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

        public override string ToString()
        {
            return Label;
        }
    }

    private sealed class LanguageItem
    {
        public LanguageItem(HelpLanguage language)
        {
            Language = language;
        }

        public HelpLanguage Language { get; }

        public override string ToString()
        {
            return Language == HelpLanguage.Japanese
                ? LocalizedTextProvider.Get(UiLanguage.Japanese, "Language.Japanese", "Japanese")
                : LocalizedTextProvider.Get(UiLanguage.English, "Language.English", "English");
        }
    }
}
